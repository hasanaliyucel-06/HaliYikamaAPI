using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using HaliYikamaAPI.Data;
using HaliYikamaAPI.Models;
using HaliYikamaAPI.Models.Enums;

namespace HaliYikamaAPI.Services.WhatsApp;

// ══════════════════════════════════════════════════════════
//  Konuşma Durum Makinesi (State Machine)
// ══════════════════════════════════════════════════════════

/// <summary>
/// Bir WhatsApp konuşmasının mevcut durumu.
/// </summary>
public enum ConversationStep
{
    /// <summary>Henüz konuşma başlamadı – hoşgeldin mesajı gönderilecek</summary>
    WaitingServiceChoice,

    /// <summary>Halı seçildi – adet soruluyor</summary>
    WaitingCarpetCount,

    /// <summary>Adres bekleniyor</summary>
    WaitingAddress,

    /// <summary>Konuşma tamamlandı – sipariş oluşturuldu</summary>
    Completed
}

/// <summary>
/// WhatsApp konuşma durum makinesi — gelen mesajları işler,
/// sonraki adımı belirler ve sipariş oluşturur.
/// </summary>
public interface IConversationStateMachine
{
    /// <summary>Gelen mesajı işleyip yanıt metni döner.</summary>
    Task<string> ProcessMessageAsync(string phoneNumber, string messageText, string? customerName = null, string? phoneNumberId = null);
}

public class ConversationStateMachine : IConversationStateMachine
{
    private sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public long LastUsedTicks = DateTime.UtcNow.Ticks;
    }

    // Her konuşma anahtarı için in-process lock (durum DB'de saklanır, idle lock'lar periyodik temizlenir)
    private static readonly ConcurrentDictionary<string, LockEntry> _locks = new();
    private static int _lockCleanupRunning;
    private static long _lastDbPruneTicks;

    // 30 dakika inaktivite sonrası konuşmayı sıfırla; 24 saatten eski yarım kalmış kayıtları DB'den temizle
    private static readonly TimeSpan ConversationTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LockIdleTimeout = TimeSpan.FromMinutes(60);
    private static readonly TimeSpan StaleConversationRetention = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConversationStateMachine> _logger;

    public ConversationStateMachine(
        IServiceScopeFactory scopeFactory,
        ILogger<ConversationStateMachine> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<string> ProcessMessageAsync(
        string phoneNumber,
        string messageText,
        string? customerName = null,
        string? phoneNumberId = null)
    {
        var input = messageText.Trim();
        var trimmedPhone = phoneNumber.Trim();
        if (trimmedPhone.Length > 30)
            trimmedPhone = trimmedPhone[..30];

        var (companyId, companyName) = await ResolveCompanyAsync(phoneNumberId);
        var conversationKey = $"{companyId:N}:{trimmedPhone}";

        var lockEntry = _locks.GetOrAdd(conversationKey, _ => new LockEntry());
        Interlocked.Exchange(ref lockEntry.LastUsedTicks, DateTime.UtcNow.Ticks);

        await lockEntry.Semaphore.WaitAsync();
        try
        {
            Interlocked.Exchange(ref lockEntry.LastUsedTicks, DateTime.UtcNow.Ticks);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var existing = await db.WhatsAppConversations
                .FirstOrDefaultAsync(c => c.CompanyId == companyId && c.PhoneNumber == trimmedPhone);

            // İptal komutu — konuşmayı güvenli şekilde sıfırla
            if (input.Equals("0", StringComparison.Ordinal) ||
                input.Equals("iptal", StringComparison.OrdinalIgnoreCase))
            {
                if (existing != null)
                {
                    db.WhatsAppConversations.Remove(existing);
                    await db.SaveChangesAsync();
                }

                _logger.LogInformation("Konuşma iptal edildi: {Phone}", trimmedPhone);
                return "🔄 Konuşma sıfırlandı.\n\n" + GetWelcomeMessage(companyName);
            }

            var reply = await ProcessMessageCoreAsync(db, existing, companyId, companyName, trimmedPhone, input, customerName);
            await TryPruneStaleConversationsAsync(db);
            return reply;
        }
        finally
        {
            lockEntry.Semaphore.Release();
            TryCleanupIdleLocks();
        }
    }

    private async Task TryPruneStaleConversationsAsync(ApplicationDbContext db)
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastDbPruneTicks);
        if (nowTicks - lastTicks < TimeSpan.FromHours(1).Ticks)
            return;

        if (Interlocked.CompareExchange(ref _lastDbPruneTicks, nowTicks, lastTicks) != lastTicks)
            return;

        try
        {
            var cutoff = DateTime.UtcNow - StaleConversationRetention;
            var stale = await db.WhatsAppConversations
                .Where(c => c.LastActivityAt < cutoff)
                .Take(200)
                .ToListAsync();

            if (stale.Count > 0)
            {
                db.WhatsAppConversations.RemoveRange(stale);
                await db.SaveChangesAsync();
                _logger.LogInformation("{Count} adet süresi dolmuş WhatsApp konuşma kaydı temizlendi.", stale.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Eski WhatsApp konuşma kayıtları temizlenirken hata oluştu.");
        }
    }

    private async Task<(Guid CompanyId, string CompanyName)> ResolveCompanyAsync(string? phoneNumberId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (!string.IsNullOrWhiteSpace(phoneNumberId))
            {
                var matched = await db.Companies
                    .AsNoTracking()
                    .Where(c => c.IsActive && c.WhatsAppPhoneNumberId == phoneNumberId)
                    .Select(c => new { c.Id, c.Name })
                    .FirstOrDefaultAsync();

                if (matched != null)
                {
                    return (matched.Id, matched.Name);
                }
            }

            var defaultCompany = await db.Companies
                .AsNoTracking()
                .Where(c => c.Id == ApplicationDbContext.DefaultCompanyId)
                .Select(c => new { c.Id, c.Name })
                .FirstOrDefaultAsync();

            if (defaultCompany != null)
            {
                return (defaultCompany.Id, defaultCompany.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp firma çözümlemesi yapılamadı, varsayılan firma kullanılıyor.");
        }

        return (ApplicationDbContext.DefaultCompanyId, "Halı ve Koltuk Yıkama");
    }

    private static void TryCleanupIdleLocks()
    {
        if (_locks.Count <= 500 || Interlocked.CompareExchange(ref _lockCleanupRunning, 1, 0) != 0)
            return;

        try
        {
            var cutoffTicks = (DateTime.UtcNow - LockIdleTimeout).Ticks;
            foreach (var kv in _locks)
            {
                if (Interlocked.Read(ref kv.Value.LastUsedTicks) < cutoffTicks &&
                    kv.Value.Semaphore.CurrentCount == 1)
                {
                    _locks.TryRemove(kv.Key, out _);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _lockCleanupRunning, 0);
        }
    }

    private async Task<string> ProcessMessageCoreAsync(
        ApplicationDbContext db,
        WhatsAppConversation? conversation,
        Guid companyId,
        string companyName,
        string phoneNumber,
        string input,
        string? customerName)
    {
        var normalizedCustomerName = string.IsNullOrWhiteSpace(customerName)
            ? null
            : (customerName.Trim().Length > 100 ? customerName.Trim()[..100] : customerName.Trim());

        var now = DateTime.UtcNow;
        var isNewConversation = false;

        if (conversation == null)
        {
            isNewConversation = true;
            conversation = new WhatsAppConversation
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                PhoneNumber = phoneNumber,
                CustomerName = normalizedCustomerName,
                Step = ConversationStep.WaitingServiceChoice,
                StartedAt = now,
                LastActivityAt = now
            };
            db.WhatsAppConversations.Add(conversation);
        }
        else if (now - conversation.LastActivityAt > ConversationTimeout)
        {
            // Zaman aşımı — 30dk inaktivite sonrası konuşmayı sıfırla (customerName korunur)
            _logger.LogInformation("Konuşma zaman aşımı, sıfırlanıyor: {Phone}", phoneNumber);
            isNewConversation = true;
            conversation.Step = ConversationStep.WaitingServiceChoice;
            conversation.ServiceType = null;
            conversation.CarpetCount = null;
            conversation.StartedAt = now;
            if (normalizedCustomerName != null)
            {
                conversation.CustomerName = normalizedCustomerName;
            }
        }
        else if (normalizedCustomerName != null)
        {
            conversation.CustomerName = normalizedCustomerName;
        }

        conversation.LastActivityAt = now;

        _logger.LogInformation(
            "Mesaj işleniyor → {Phone} | Firma: {CompanyName} | Adım: {Step} | Yeni: {IsNew} | Giriş: {Input}",
            phoneNumber, companyName, conversation.Step, isNewConversation, input);

        try
        {
            string reply = conversation.Step switch
            {
                ConversationStep.WaitingServiceChoice => HandleServiceChoice(conversation, companyName, input, isNewConversation),
                ConversationStep.WaitingCarpetCount => HandleCarpetCount(conversation, input),
                ConversationStep.WaitingAddress => await HandleAddressAsync(db, conversation, input),
                ConversationStep.Completed => HandleCompleted(db, conversation, companyName),
                _ => GetWelcomeMessage(companyName)
            };

            if (db.ChangeTracker.HasChanges())
            {
                await db.SaveChangesAsync();
            }

            return reply;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Konuşma işleme hatası: {Phone}", phoneNumber);
            return "⚠️ Bir hata oluştu. Lütfen tekrar deneyin veya bizi arayın.";
        }
    }

    // ────────────────────────────────────────────
    //  Adım 1: Hizmet Seçimi
    // ────────────────────────────────────────────

    private string HandleServiceChoice(WhatsAppConversation conv, string companyName, string input, bool isNewConversation)
    {
        switch (input)
        {
            case "1":
                conv.ServiceType = ServiceType.Hali;
                conv.Step = ConversationStep.WaitingCarpetCount;
                _logger.LogInformation("Hizmet seçildi: Halı → {Phone}", conv.PhoneNumber);
                return "🧹 Halı yıkama seçildi.\n\n📦 Kaç adet halı alınacak? (Lütfen sadece sayı yazın)";

            case "2":
                conv.ServiceType = ServiceType.Koltuk;
                conv.Step = ConversationStep.WaitingAddress;
                _logger.LogInformation("Hizmet seçildi: Koltuk → {Phone}", conv.PhoneNumber);
                return "🛋️ Yerinde koltuk yıkama seçildi.\n\n📍 Açık adresinizi yazın:";

            default:
                // İlk selamlaşma mesajında hata göstermeden doğrudan hoşgeldin menüsünü gönder
                if (isNewConversation)
                {
                    return GetWelcomeMessage(companyName);
                }

                return "❌ Geçersiz seçim.\n\n"
                     + GetWelcomeMessage(companyName);
        }
    }

    // ────────────────────────────────────────────
    //  Adım 2: Halı Adedi (sadece Halı seçildiyse)
    // ────────────────────────────────────────────

    private string HandleCarpetCount(WhatsAppConversation conv, string input)
    {
        if (!int.TryParse(input, out var count))
        {
            return "❌ Lütfen geçerli bir sayı girin.\n\n📦 Kaç adet halı alınacak?";
        }

        if (count <= 0)
        {
            return "❌ Halı adedi 0'dan büyük olmalıdır.\n\n📦 Kaç adet halı alınacak?";
        }

        if (count > 100)
        {
            return "❌ Maksimum 100 adet girilebilir.\n\n📦 Kaç adet halı alınacak?";
        }

        conv.CarpetCount = count;
        conv.Step = ConversationStep.WaitingAddress;

        _logger.LogInformation("Halı adedi: {Count} → {Phone}", count, conv.PhoneNumber);
        return $"✅ {count} adet halı kaydedildi.\n\n📍 Açık adresinizi yazın:";
    }

    // ────────────────────────────────────────────
    //  Adım 3: Adres → Sipariş Oluştur
    // ────────────────────────────────────────────

    private async Task<string> HandleAddressAsync(ApplicationDbContext db, WhatsAppConversation conv, string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length < 10)
        {
            return "❌ Adres çok kısa. Lütfen mahalle, sokak ve kapı numarası içeren açık adresinizi yazın:";
        }

        if (input.Length > 500)
        {
            return "❌ Adres çok uzun (en fazla 500 karakter). Lütfen daha kısa ve net bir adres yazın:";
        }

        // ── Veritabanına sipariş oluştur ve konuşmayı atomik olarak temizle ──
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CompanyId = conv.CompanyId,
            CustomerName = conv.CustomerName,
            CustomerPhone = NormalizePhoneForDb(conv.PhoneNumber),
            CustomerAddress = input,
            ServiceType = conv.ServiceType ?? ServiceType.Hali,
            CarpetCount = conv.CarpetCount,
            ReceiptNumber = null,
            TotalPrice = null,
            RequiresPOS = false,
            Status = OrderStatus.YeniTalep,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            db.Orders.Add(order);
            db.WhatsAppConversations.Remove(conv);
            await db.SaveChangesAsync();

            _logger.LogInformation(
                "Sipariş oluşturuldu via WhatsApp → {OrderId} | Firma: {CompanyId} | {Phone}",
                order.Id, conv.CompanyId, conv.PhoneNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp sipariş oluşturma hatası: {Phone}", conv.PhoneNumber);
            db.Entry(order).State = EntityState.Detached;
            db.Entry(conv).State = EntityState.Unchanged;
            return "⚠️ Sipariş kaydedilirken bir hata oluştu. Lütfen tekrar deneyin veya bizi arayın.";
        }

        var serviceLabel = conv.ServiceType == ServiceType.Koltuk ? "Koltuk yıkama" : "Halı yıkama";
        var countInfo = conv.CarpetCount.HasValue ? $"\n📦 Adet: {conv.CarpetCount}" : "";

        return $"✅ Siparişiniz başarıyla oluşturuldu!\n\n"
             + $"📋 Kayıt numaranız: {order.Id}\n"
             + $"🏷️ Hizmet: {serviceLabel}{countInfo}\n"
             + $"📍 Adres: {input}\n\n"
             + $"Ekiplerimiz en kısa sürede size ulaşacaktır. Teşekkür ederiz! 🙏";
    }

    // ────────────────────────────────────────────
    //  Tamamlanmış konuşma — yeni başlat
    // ────────────────────────────────────────────

    private string HandleCompleted(ApplicationDbContext db, WhatsAppConversation conv, string companyName)
    {
        db.WhatsAppConversations.Remove(conv);
        return GetWelcomeMessage(companyName);
    }

    // ────────────────────────────────────────────
    //  Yardımcılar
    // ────────────────────────────────────────────

    private static string GetWelcomeMessage(string companyName)
    {
        return $"{companyName}'ya hoş geldiniz! 👋\n\n"
             + "🧹 Halı yıkama için *1*\n"
             + "🛋️ Yerinde Koltuk yıkama için *2*\n\n"
             + "yazın.";
    }

    /// <summary>
    /// WhatsApp formatındaki telefon numarasını (ör. 905551234567)
    /// veritabanı formatına (ör. 05551234567, maks. 20 karakter) çevirir.
    /// </summary>
    private static string NormalizePhoneForDb(string waPhone)
    {
        var normalized = waPhone.Trim();
        if (normalized.StartsWith("90") && normalized.Length >= 12)
        {
            normalized = "0" + normalized[2..];
        }

        return normalized.Length > 20 ? normalized[..20] : normalized;
    }
}

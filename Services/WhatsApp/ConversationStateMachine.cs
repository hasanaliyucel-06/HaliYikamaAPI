using System.Collections.Concurrent;
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
/// Aktif konuşma verisi.
/// </summary>
public class ConversationState
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string? CustomerName { get; set; }
    public ConversationStep Step { get; set; } = ConversationStep.WaitingServiceChoice;
    public ServiceType? ServiceType { get; set; }
    public int? CarpetCount { get; set; }
    public string? Address { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// WhatsApp konuşma durum makinesi — gelen mesajları işler,
/// sonraki adımı belirler ve sipariş oluşturur.
/// </summary>
public interface IConversationStateMachine
{
    /// <summary>Gelen mesajı işleyip yanıt metni döner.</summary>
    Task<string> ProcessMessageAsync(string phoneNumber, string messageText, string? customerName = null);
}

public class ConversationStateMachine : IConversationStateMachine
{
    // Thread-safe in-memory store — her telefon numarası için bir konuşma
    private static readonly ConcurrentDictionary<string, ConversationState> _conversations = new();

    // Her telefon numarası için lock (thread-safety)
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    // 30 dakika inaktivite sonrası konuşmayı sıfırla
    private static readonly TimeSpan ConversationTimeout = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConversationStateMachine> _logger;

    public ConversationStateMachine(
        IServiceScopeFactory scopeFactory,
        ILogger<ConversationStateMachine> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<string> ProcessMessageAsync(string phoneNumber, string messageText, string? customerName = null)
    {
        var input = messageText.Trim();

        // İptal komutu — konuşmayı sıfırla
        if (input.Equals("0", StringComparison.Ordinal) ||
            input.Equals("iptal", StringComparison.OrdinalIgnoreCase))
        {
            _conversations.TryRemove(phoneNumber, out _);
            _logger.LogInformation("Konuşma iptal edildi: {Phone}", phoneNumber);
            return "🔄 Konuşma sıfırlandı.\n\n" + GetWelcomeMessage();
        }

        // Her telefon numarası için kendi lock'unu al (concurrent mesajlarda state bozulmasını önle)
        var phoneLock = _locks.GetOrAdd(phoneNumber, _ => new SemaphoreSlim(1, 1));
        await phoneLock.WaitAsync();
        try
        {
            return await ProcessMessageCoreAsync(phoneNumber, input, customerName);
        }
        finally
        {
            phoneLock.Release();
        }
    }

    private async Task<string> ProcessMessageCoreAsync(string phoneNumber, string input, string? customerName)
    {
        // Mevcut konuşmayı al veya yeni oluştur
        var conversation = _conversations.GetOrAdd(phoneNumber, _ => new ConversationState
        {
            PhoneNumber = phoneNumber,
            CustomerName = customerName
        });

        if (!string.IsNullOrWhiteSpace(customerName))
        {
            conversation.CustomerName = customerName;
        }

        // Zaman aşımı kontrolü — 30dk inaktivite sonrası sıfırla
        if (DateTime.UtcNow - conversation.LastActivityAt > ConversationTimeout)
        {
            _logger.LogInformation("Konuşma zaman aşımı, sıfırlanıyor: {Phone}", phoneNumber);
            conversation = new ConversationState { PhoneNumber = phoneNumber };
            _conversations[phoneNumber] = conversation;
        }

        conversation.LastActivityAt = DateTime.UtcNow;

        _logger.LogInformation(
            "Mesaj işleniyor → {Phone} | Adım: {Step} | Giriş: {Input}",
            phoneNumber, conversation.Step, input);

        try
        {
            return conversation.Step switch
            {
                ConversationStep.WaitingServiceChoice => HandleServiceChoice(conversation, input),
                ConversationStep.WaitingCarpetCount => HandleCarpetCount(conversation, input),
                ConversationStep.WaitingAddress => await HandleAddressAsync(conversation, input),
                ConversationStep.Completed => HandleCompleted(conversation),
                _ => GetWelcomeMessage()
            };
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

    private string HandleServiceChoice(ConversationState conv, string input)
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
                return "❌ Geçersiz seçim.\n\n"
                     + GetWelcomeMessage();
        }
    }

    // ────────────────────────────────────────────
    //  Adım 2: Halı Adedi (sadece Halı seçildiyse)
    // ────────────────────────────────────────────

    private string HandleCarpetCount(ConversationState conv, string input)
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

    private async Task<string> HandleAddressAsync(ConversationState conv, string input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length < 10)
        {
            return "❌ Adres çok kısa. Lütfen mahalle, sokak ve kapı numarası içeren açık adresinizi yazın:";
        }

        conv.Address = input;
        conv.Step = ConversationStep.Completed;

        // ── Veritabanına sipariş oluştur ──
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = conv.CustomerName,
            CustomerPhone = NormalizePhoneForDb(conv.PhoneNumber),
            CustomerAddress = conv.Address,
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
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            dbContext.Orders.Add(order);
            await dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Sipariş oluşturuldu via WhatsApp → {OrderId} | {Phone}",
                order.Id, conv.PhoneNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WhatsApp sipariş oluşturma hatası: {Phone}", conv.PhoneNumber);
            conv.Step = ConversationStep.WaitingAddress;
            return "⚠️ Sipariş kaydedilirken bir hata oluştu. Lütfen tekrar deneyin veya bizi arayın.";
        }

        // Konuşmayı temizle (yeni sipariş için hazır)
        _conversations.TryRemove(conv.PhoneNumber, out _);

        var serviceLabel = conv.ServiceType == ServiceType.Koltuk ? "Koltuk yıkama" : "Halı yıkama";
        var countInfo = conv.CarpetCount.HasValue ? $"\n📦 Adet: {conv.CarpetCount}" : "";

        return $"✅ Siparişiniz başarıyla oluşturuldu!\n\n"
             + $"📋 Kayıt numaranız: {order.Id}\n"
             + $"🏷️ Hizmet: {serviceLabel}{countInfo}\n"
             + $"📍 Adres: {conv.Address}\n\n"
             + $"Ekiplerimiz en kısa sürede size ulaşacaktır. Teşekkür ederiz! 🙏";
    }

    // ────────────────────────────────────────────
    //  Tamamlanmış konuşma — yeni başlat
    // ────────────────────────────────────────────

    private string HandleCompleted(ConversationState conv)
    {
        // Tamamlanmış konuşmayı sıfırla
        _conversations.TryRemove(conv.PhoneNumber, out _);
        return GetWelcomeMessage();
    }

    // ────────────────────────────────────────────
    //  Yardımcılar
    // ────────────────────────────────────────────

    private static string GetWelcomeMessage()
    {
        return "Deniz Halı ve Koltuk Yıkama'ya hoş geldiniz! 👋\n\n"
             + "🧹 Halı yıkama için *1*\n"
             + "🛋️ Yerinde Koltuk yıkama için *2*\n\n"
             + "yazın.";
    }

    /// <summary>
    /// WhatsApp formatındaki telefon numarasını (ör. 905551234567)
    /// veritabanı formatına (ör. 05551234567) çevirir.
    /// </summary>
    private static string NormalizePhoneForDb(string waPhone)
    {
        // WhatsApp numarası: 905551234567 → 05551234567
        if (waPhone.StartsWith("90") && waPhone.Length >= 12)
        {
            return "0" + waPhone[2..];
        }
        return waPhone;
    }
}

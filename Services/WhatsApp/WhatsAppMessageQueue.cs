using System.Collections.Concurrent;
using System.Threading.Channels;
using HaliYikamaAPI.DTOs.WhatsApp;

namespace HaliYikamaAPI.Services.WhatsApp;

/// <summary>
/// WhatsApp webhook mesajlarını arka planda işlemek için asenkron kuyruk arayüzü.
/// </summary>
public interface IWhatsAppMessageQueue
{
    ValueTask EnqueueAsync(WhatsAppWebhookPayload payload, CancellationToken cancellationToken = default);
    IAsyncEnumerable<WhatsAppWebhookPayload> ReadAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Channel tabanlı yüksek performanslı bellek içi mesaj kuyruğu.
/// </summary>
public class WhatsAppMessageQueue : IWhatsAppMessageQueue
{
    private readonly Channel<WhatsAppWebhookPayload> _channel;

    public WhatsAppMessageQueue()
    {
        // 5000 mesaj kapasiteli bounded channel — backpressure kontrolü
        var options = new BoundedChannelOptions(5000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<WhatsAppWebhookPayload>(options);
    }

    public ValueTask EnqueueAsync(WhatsAppWebhookPayload payload, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(payload, cancellationToken);
    }

    public IAsyncEnumerable<WhatsAppWebhookPayload> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}

/// <summary>
/// Kuyruktaki WhatsApp mesajlarını tüketen ve sırayla işleyen arka plan servisi.
/// </summary>
public class WhatsAppBackgroundWorker : BackgroundService
{
    private readonly IWhatsAppMessageQueue _queue;
    private readonly IConversationStateMachine _stateMachine;
    private readonly IWhatsAppSender _sender;
    private readonly ILogger<WhatsAppBackgroundWorker> _logger;

    // Mesaj tekrarı önleme — FIFO idempotency cache (thread-safe)
    private static readonly ConcurrentDictionary<string, DateTime> _processedMessageIds = new();
    private const int MaxCacheSize = 10_000;
    private static int _cleanupRunning;

    public WhatsAppBackgroundWorker(
        IWhatsAppMessageQueue queue,
        IConversationStateMachine stateMachine,
        IWhatsAppSender sender,
        ILogger<WhatsAppBackgroundWorker> logger)
    {
        _queue = queue;
        _stateMachine = stateMachine;
        _sender = sender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("WhatsApp arka plan mesaj işleyici başlatıldı.");

        try
        {
            await foreach (var payload in _queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessPayloadAsync(payload);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "WhatsApp payload işlenirken beklenmeyen hata oluştu");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host durdurulurken normal kapanma
        }

        _logger.LogInformation("WhatsApp arka plan mesaj işleyici durduruldu.");
    }

    private async Task ProcessPayloadAsync(WhatsAppWebhookPayload payload)
    {
        foreach (var entry in payload.Entry)
        {
            foreach (var change in entry.Changes)
            {
                if (change.Field != "messages")
                    continue;

                var contacts = change.Value.Contacts;
                var messages = change.Value.Messages;
                if (messages is null || messages.Count == 0)
                    continue;

                foreach (var message in messages)
                {
                    var senderName = contacts?.FirstOrDefault(c => c.WaId == message.From)?.Profile?.Name;
                    await ProcessSingleMessageAsync(message, senderName);
                }
            }
        }
    }

    private async Task ProcessSingleMessageAsync(WebhookMessage message, string? senderName = null)
    {
        // ── Tekrar kontrolü (idempotency) — FIFO cache ──
        if (!_processedMessageIds.TryAdd(message.Id, DateTime.UtcNow))
        {
            _logger.LogInformation("Tekrarlanan mesaj atlandı: {MessageId}", message.Id);
            return;
        }

        // Eski kayıtları periyodik temizle (MaxCacheSize aşıldığında)
        if (_processedMessageIds.Count > MaxCacheSize &&
            Interlocked.CompareExchange(ref _cleanupRunning, 1, 0) == 0)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddMinutes(-60);
                var oldKeys = _processedMessageIds
                    .Where(kv => kv.Value < cutoff)
                    .Select(kv => kv.Key)
                    .ToList();
                foreach (var key in oldKeys)
                    _processedMessageIds.TryRemove(key, out _);
            }
            finally
            {
                Interlocked.Exchange(ref _cleanupRunning, 0);
            }
        }

        // ── Sadece text mesajlarını işle ──
        if (!string.Equals(message.Type, "text", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Text olmayan mesaj atlandı: {Type} from {From}", message.Type, message.From);
            return;
        }

        var senderPhone = message.From;
        var messageText = message.Text?.Body ?? string.Empty;

        if (string.IsNullOrWhiteSpace(messageText))
        {
            _logger.LogInformation("Boş mesaj atlandı: {From}", senderPhone);
            return;
        }

        _logger.LogInformation(
            "Gelen WhatsApp mesajı → {From} ({Name}): {Text}",
            senderPhone, senderName ?? "İsimsiz", messageText);

        try
        {
            var response = await _stateMachine.ProcessMessageAsync(senderPhone, messageText, senderName);
            await _sender.SendTextMessageAsync(senderPhone, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mesaj işleme hatası: {From}", senderPhone);

            try
            {
                await _sender.SendTextMessageAsync(senderPhone,
                    "⚠️ Bir hata oluştu. Lütfen tekrar deneyin.");
            }
            catch
            {
                // Hata bildirimi de başarısızsa loglandı, devam et
            }
        }
    }
}

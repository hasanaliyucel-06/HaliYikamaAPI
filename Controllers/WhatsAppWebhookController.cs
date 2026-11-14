using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using HaliYikamaAPI.DTOs.WhatsApp;
using HaliYikamaAPI.Services.WhatsApp;

namespace HaliYikamaAPI.Controllers;

/// <summary>
/// Meta WhatsApp Business Cloud API webhook controller.
/// GET  → Webhook doğrulama (Verify Token handshake)
/// POST → Gelen mesajları kuyruğa alma (Asenkron arka plan işleme)
/// </summary>
[ApiController]
[Route("api/whatsapp/webhook")]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly WhatsAppSettings _settings;
    private readonly IWebhookSignatureValidator _signatureValidator;
    private readonly IWhatsAppMessageQueue _queue;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        IOptions<WhatsAppSettings> settings,
        IWebhookSignatureValidator signatureValidator,
        IWhatsAppMessageQueue queue,
        ILogger<WhatsAppWebhookController> logger)
    {
        _settings = settings.Value;
        _signatureValidator = signatureValidator;
        _queue = queue;
        _logger = logger;
    }

    // ══════════════════════════════════════════════════
    //  GET: Webhook Doğrulama (Verify Token Handshake)
    // ══════════════════════════════════════════════════

    /// <summary>
    /// Meta App Dashboard webhook doğrulaması.
    /// hub.mode=subscribe, hub.verify_token ve hub.challenge parametrelerini kontrol eder.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string? hubMode,
        [FromQuery(Name = "hub.verify_token")] string? hubVerifyToken,
        [FromQuery(Name = "hub.challenge")] string? hubChallenge)
    {
        _logger.LogInformation(
            "Webhook doğrulama isteği → Mode: {Mode}, Token: {Token}",
            hubMode, hubVerifyToken != null ? "***" : "null");

        // 1) hub.mode "subscribe" olmalı
        if (!string.Equals(hubMode, "subscribe", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Geçersiz hub.mode: {Mode}", hubMode);
            return StatusCode(StatusCodes.Status403Forbidden, "Invalid hub.mode");
        }

        // 2) Verify token eşleşmeli
        if (string.IsNullOrEmpty(hubVerifyToken) ||
            !string.Equals(hubVerifyToken, _settings.VerifyToken, StringComparison.Ordinal))
        {
            _logger.LogWarning("Verify token eşleşmedi!");
            return StatusCode(StatusCodes.Status403Forbidden, "Invalid verify token");
        }

        // 3) Challenge'ı düz metin olarak döndür
        if (string.IsNullOrEmpty(hubChallenge))
        {
            _logger.LogWarning("hub.challenge parametresi eksik");
            return StatusCode(StatusCodes.Status403Forbidden, "Missing challenge");
        }

        _logger.LogInformation("Webhook doğrulaması başarılı ✓");
        return Content(hubChallenge, "text/plain");
    }

    // ══════════════════════════════════════════════════
    //  POST: Gelen Mesaj İşleme
    // ══════════════════════════════════════════════════

    /// <summary>
    /// Meta'dan gelen webhook POST isteğini doğrular ve arka plan kuyruğuna ekler.
    /// X-Hub-Signature-256 doğrulaması yapar, Meta'ya anında 200 döner.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReceiveMessage()
    {
        // ── 1) Ham gövdeyi oku (imza doğrulaması için) ──
        Request.EnableBuffering();
        using var memStream = new MemoryStream();
        await Request.Body.CopyToAsync(memStream);
        var bodyBytes = memStream.ToArray();
        Request.Body.Position = 0;

        // ── 2) X-Hub-Signature-256 doğrulaması ──
        var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        if (!_signatureValidator.IsValid(bodyBytes, signature))
        {
            _logger.LogWarning("Webhook imza doğrulaması başarısız — istek reddedildi");
            // Meta'ya 200 döndür (tekrar denemeyi engelle), ancak işleme
            return Ok();
        }

        // ── 3) JSON parse ──
        WhatsAppWebhookPayload? payload;
        try
        {
            var bodyString = System.Text.Encoding.UTF8.GetString(bodyBytes);
            payload = JsonSerializer.Deserialize<WhatsAppWebhookPayload>(bodyString);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Webhook JSON parse hatası");
            return Ok(); // Meta'ya 200 döndür
        }

        if (payload is null)
        {
            _logger.LogWarning("Webhook payload null");
            return Ok();
        }

        // ── 4) Kuyruğa ekle ve anında 200 dön ──
        await _queue.EnqueueAsync(payload);
        return Ok();
    }
}

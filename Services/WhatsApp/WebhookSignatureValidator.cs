using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HaliYikamaAPI.Services.WhatsApp;

/// <summary>
/// Meta X-Hub-Signature-256 header doğrulaması.
/// Gelen POST isteğinin gerçekten Meta'dan geldiğini kriptografik olarak doğrular.
/// </summary>
public interface IWebhookSignatureValidator
{
    /// <summary>
    /// X-Hub-Signature-256 header'ını doğrular.
    /// </summary>
    /// <param name="requestBody">Ham istek gövdesi (byte[])</param>
    /// <param name="signatureHeader">X-Hub-Signature-256 header değeri</param>
    /// <returns>Doğrulama sonucu</returns>
    bool IsValid(byte[] requestBody, string? signatureHeader);
}

public class WebhookSignatureValidator : IWebhookSignatureValidator
{
    private readonly WhatsAppSettings _settings;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<WebhookSignatureValidator> _logger;

    public WebhookSignatureValidator(
        IOptions<WhatsAppSettings> settings,
        IHostEnvironment environment,
        ILogger<WebhookSignatureValidator> logger)
    {
        _settings = settings.Value;
        _environment = environment;
        _logger = logger;
    }

    public bool IsValid(byte[] requestBody, string? signatureHeader)
    {
        var isUnconfigured = string.IsNullOrWhiteSpace(_settings.AppSecret) ||
                             _settings.AppSecret == "REPLACE_WITH_ENV_VAR_OR_USER_SECRET";

        if (isUnconfigured)
        {
            if (_environment.IsDevelopment())
            {
                _logger.LogWarning("AppSecret yapılandırılmamış (Development) — imza doğrulaması atlanıyor!");
                return true;
            }

            _logger.LogError("AppSecret production ortamında yapılandırılmamış — webhook isteği reddedildi!");
            return false;
        }

        if (string.IsNullOrEmpty(signatureHeader))
        {
            _logger.LogWarning("X-Hub-Signature-256 header eksik");
            return false;
        }

        // Header formatı: "sha256=<hex_digest>"
        const string prefix = "sha256=";
        if (!signatureHeader.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Geçersiz imza formatı: {Header}", signatureHeader);
            return false;
        }

        var receivedHash = signatureHeader[prefix.Length..];

        // HMAC-SHA256 hesapla
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_settings.AppSecret));
        var computedHashBytes = hmac.ComputeHash(requestBody);
        var computedHash = Convert.ToHexStringLower(computedHashBytes);

        // Timing-safe karşılaştırma
        var isValid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHash),
            Encoding.UTF8.GetBytes(receivedHash.ToLowerInvariant()));

        if (!isValid)
        {
            _logger.LogWarning("İmza doğrulaması başarısız!");
        }

        return isValid;
    }
}

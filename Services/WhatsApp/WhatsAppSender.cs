using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using HaliYikamaAPI.DTOs.WhatsApp;

namespace HaliYikamaAPI.Services.WhatsApp;

/// <summary>
/// Meta WhatsApp Cloud API üzerinden mesaj gönderme servisi.
/// </summary>
public interface IWhatsAppSender
{
    /// <summary>Belirtilen numaraya metin mesajı gönderir.</summary>
    Task SendTextMessageAsync(string recipientPhone, string message, string? phoneNumberIdOverride = null);
}

public class WhatsAppSender : IWhatsAppSender
{
    private readonly HttpClient _httpClient;
    private readonly WhatsAppSettings _settings;
    private readonly ILogger<WhatsAppSender> _logger;

    public WhatsAppSender(
        HttpClient httpClient,
        IOptions<WhatsAppSettings> settings,
        ILogger<WhatsAppSender> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task SendTextMessageAsync(string recipientPhone, string message, string? phoneNumberIdOverride = null)
    {
        var phoneNumberId = !string.IsNullOrWhiteSpace(phoneNumberIdOverride)
            ? phoneNumberIdOverride.Trim()
            : _settings.PhoneNumberId;

        var url = $"https://graph.facebook.com/{_settings.ApiVersion}/{phoneNumberId}/messages";

        var payload = new SendMessageRequest
        {
            MessagingProduct = "whatsapp",
            RecipientType = "individual",
            To = recipientPhone,
            Type = "text",
            Text = new SendMessageText { Body = message }
        };

        var json = JsonSerializer.Serialize(payload);
        using var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

        // Her istekte yeni HttpRequestMessage kullan (thread-safe) ve düzgün dispose et
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = httpContent
        };
        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", _settings.AccessToken);

        try
        {
            using var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "WhatsApp mesaj gönderildi → {Phone} | Yanıt: {Response}",
                    recipientPhone, responseBody);
            }
            else
            {
                _logger.LogError(
                    "WhatsApp mesaj gönderilemedi → {Phone} | HTTP {StatusCode} | Yanıt: {Response}",
                    recipientPhone, (int)response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "WhatsApp mesaj gönderme hatası → {Phone}", recipientPhone);
        }
    }
}

using System.Text.Json.Serialization;

namespace HaliYikamaAPI.DTOs.WhatsApp;

// ══════════════════════════════════════════════════════
//  Meta WhatsApp Cloud API – Webhook Payload DTOs
//  Ref: https://developers.facebook.com/docs/whatsapp/cloud-api/webhooks/payload-examples
// ══════════════════════════════════════════════════════

/// <summary>
/// Webhook üst düzey payload — Meta Cloud API v22.0+
/// </summary>
public class WhatsAppWebhookPayload
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = string.Empty;

    [JsonPropertyName("entry")]
    public List<WebhookEntry> Entry { get; set; } = [];
}

public class WebhookEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("changes")]
    public List<WebhookChange> Changes { get; set; } = [];
}

public class WebhookChange
{
    [JsonPropertyName("value")]
    public ChangeValue Value { get; set; } = new();

    [JsonPropertyName("field")]
    public string Field { get; set; } = string.Empty;
}

public class ChangeValue
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; set; } = string.Empty;

    [JsonPropertyName("metadata")]
    public MessageMetadata Metadata { get; set; } = new();

    [JsonPropertyName("contacts")]
    public List<WebhookContact>? Contacts { get; set; }

    [JsonPropertyName("messages")]
    public List<WebhookMessage>? Messages { get; set; }

    [JsonPropertyName("statuses")]
    public List<WebhookStatus>? Statuses { get; set; }
}

public class MessageMetadata
{
    [JsonPropertyName("display_phone_number")]
    public string DisplayPhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("phone_number_id")]
    public string PhoneNumberId { get; set; } = string.Empty;
}

public class WebhookContact
{
    [JsonPropertyName("profile")]
    public ContactProfile Profile { get; set; } = new();

    [JsonPropertyName("wa_id")]
    public string WaId { get; set; } = string.Empty;
}

public class ContactProfile
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}

public class WebhookMessage
{
    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public MessageText? Text { get; set; }
}

public class MessageText
{
    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;
}

public class WebhookStatus
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = string.Empty;

    [JsonPropertyName("recipient_id")]
    public string RecipientId { get; set; } = string.Empty;
}

// ══════════════════════════════════════════════════════
//  Mesaj Gönderme İsteği DTO'su
// ══════════════════════════════════════════════════════

public class SendMessageRequest
{
    [JsonPropertyName("messaging_product")]
    public string MessagingProduct { get; set; } = "whatsapp";

    [JsonPropertyName("recipient_type")]
    public string RecipientType { get; set; } = "individual";

    [JsonPropertyName("to")]
    public string To { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public SendMessageText Text { get; set; } = new();
}

public class SendMessageText
{
    [JsonPropertyName("preview_url")]
    public bool PreviewUrl { get; set; } = false;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;
}

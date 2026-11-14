namespace HaliYikamaAPI.Services.WhatsApp;

/// <summary>
/// WhatsApp API yapılandırma ayarları.
/// appsettings.json -> "WhatsApp" bölümünden bind edilir.
/// </summary>
public class WhatsAppSettings
{
    public const string SectionName = "WhatsApp";

    /// <summary>Meta App Dashboard'da tanımlanan Verify Token</summary>
    public string VerifyToken { get; set; } = string.Empty;

    /// <summary>WhatsApp Cloud API erişim tokeni</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>WhatsApp Business telefon numarası ID'si</summary>
    public string PhoneNumberId { get; set; } = string.Empty;

    /// <summary>Meta App Secret — X-Hub-Signature-256 doğrulaması için</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Graph API sürümü (varsayılan: v22.0)</summary>
    public string ApiVersion { get; set; } = "v22.0";
}

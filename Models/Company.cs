using System.ComponentModel.DataAnnotations;

namespace HaliYikamaAPI.Models;

/// <summary>
/// Halı ve Koltuk Yıkama İşletmesi (Tenant).
/// </summary>
public class Company
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>İşletme adı (örn: Deniz Halı Yıkama)</summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>Giriş e-posta adresi</summary>
    [Required]
    [EmailAddress]
    [StringLength(100)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Şifre hash'i</summary>
    [Required]
    [StringLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Firma iletişim telefonu</summary>
    [Required]
    [Phone]
    [StringLength(20)]
    public string Phone { get; set; } = string.Empty;

    /// <summary>Firma açık adresi</summary>
    [StringLength(250)]
    public string? Address { get; set; }

    /// <summary>Sürücü kodları için ön ek (örn: DNZ)</summary>
    [Required]
    [StringLength(10)]
    public string CodePrefix { get; set; } = "DNZ";

    /// <summary>Meta WhatsApp Business Phone Number ID (çoklu firma yönlendirmesi için opsiyonel)</summary>
    [StringLength(50)]
    public string? WhatsAppPhoneNumberId { get; set; }

    /// <summary>Firma aktif mi?</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Oluşturulma tarihi</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<Driver> Drivers { get; set; } = [];
    public ICollection<Order> Orders { get; set; } = [];
}

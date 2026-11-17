using System.ComponentModel.DataAnnotations;

namespace HaliYikamaAPI.DTOs;

/// <summary>
/// Firma profil yanıt DTO'su.
/// </summary>
public class CompanyProfileDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string CodePrefix { get; set; } = string.Empty;
    public string? WhatsAppPhoneNumberId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Firma profil güncelleme DTO'su.
/// </summary>
public class UpdateCompanyProfileDto
{
    [Required(ErrorMessage = "Firma adı zorunludur.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Firma adı en az 2, en fazla 100 karakter olmalıdır.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Telefon numarası zorunludur.")]
    [Phone(ErrorMessage = "Geçerli bir telefon numarası giriniz.")]
    [StringLength(20)]
    public string Phone { get; set; } = string.Empty;

    [StringLength(250)]
    public string? Address { get; set; }

    /// <summary>Meta WhatsApp Business Phone Number ID (opsiyonel)</summary>
    [StringLength(50)]
    public string? WhatsAppPhoneNumberId { get; set; }
}

/// <summary>
/// Firma yönetici şifre değiştirme DTO'su.
/// </summary>
public class ChangePasswordDto
{
    [Required(ErrorMessage = "Mevcut şifre zorunludur.")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Yeni şifre zorunludur.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Yeni şifre en az 6 karakter olmalıdır.")]
    public string NewPassword { get; set; } = string.Empty;
}

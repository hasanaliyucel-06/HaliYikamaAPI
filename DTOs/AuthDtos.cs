using System.ComponentModel.DataAnnotations;

namespace HaliYikamaAPI.DTOs;

/// <summary>
/// Firma yönetim girişi DTO'su.
/// </summary>
public class CompanyLoginDto
{
    [Required(ErrorMessage = "E-posta adresi zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre zorunludur.")]
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Sürücü hızlı kod girişi DTO'su.
/// </summary>
public class DriverLoginDto
{
    [Required(ErrorMessage = "Sürücü kodu zorunludur.")]
    public string AccessCode { get; set; } = string.Empty;
}

/// <summary>
/// Başarılı giriş yanıt DTO'su.
/// </summary>
public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // "CompanyAdmin" veya "Driver"
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
}

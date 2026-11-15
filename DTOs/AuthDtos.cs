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
/// Yeni firma kayıt DTO'su.
/// </summary>
public class CompanyRegisterDto
{
    [Required(ErrorMessage = "Firma adı zorunludur.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Firma adı en az 2, en fazla 100 karakter olmalıdır.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-posta adresi zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    [StringLength(100)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Şifre en az 6 karakter olmalıdır.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Telefon numarası zorunludur.")]
    [Phone(ErrorMessage = "Geçerli bir telefon numarası giriniz.")]
    [StringLength(20)]
    public string Phone { get; set; } = string.Empty;

    [StringLength(250)]
    public string? Address { get; set; }

    /// <summary>Sürücü kod ön eki (örn: DNZ, AKY). Boş bırakılırsa firma adından otomatik üretilir.</summary>
    [StringLength(5, MinimumLength = 2, ErrorMessage = "Firma ön eki 2-5 karakter arası olmalıdır.")]
    public string? CodePrefix { get; set; }
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

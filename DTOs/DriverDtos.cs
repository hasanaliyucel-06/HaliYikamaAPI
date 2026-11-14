using System.ComponentModel.DataAnnotations;

namespace HaliYikamaAPI.DTOs;

/// <summary>
/// Yeni sürücü ekleme DTO'su.
/// </summary>
public class CreateDriverDto
{
    [Required(ErrorMessage = "Sürücü adı soyadı zorunludur.")]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Telefon numarası zorunludur.")]
    [Phone(ErrorMessage = "Geçerli bir telefon numarası giriniz.")]
    [StringLength(20)]
    public string Phone { get; set; } = string.Empty;

    /// <summary>Özel kod girilmezse firma ön ekiyle otomatik üretilir</summary>
    [StringLength(20)]
    public string? AccessCode { get; set; }

    [StringLength(20)]
    public string? VehiclePlate { get; set; }
}

/// <summary>
/// Sürücü güncelleme DTO'su.
/// </summary>
public class UpdateDriverDto
{
    [Required(ErrorMessage = "Sürücü adı soyadı zorunludur.")]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Telefon numarası zorunludur.")]
    [Phone(ErrorMessage = "Geçerli bir telefon numarası giriniz.")]
    [StringLength(20)]
    public string Phone { get; set; } = string.Empty;

    [StringLength(20)]
    public string? VehiclePlate { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Sürücü listeleme yanıt DTO'su.
/// </summary>
public class DriverResponseDto
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string AccessCode { get; set; } = string.Empty;
    public string? VehiclePlate { get; set; }
    public bool IsActive { get; set; }
    public int ActiveOrderCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

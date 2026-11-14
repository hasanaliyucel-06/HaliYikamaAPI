using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HaliYikamaAPI.Models;

/// <summary>
/// İşletmeye bağlı servis ve teslimat sürücüsü.
/// </summary>
public class Driver
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>Bağlı olduğu firma ID'si</summary>
    [Required]
    public Guid CompanyId { get; set; }

    [ForeignKey(nameof(CompanyId))]
    public Company? Company { get; set; }

    /// <summary>Sürücü ad soyad</summary>
    [Required]
    [StringLength(100)]
    public string FullName { get; set; } = string.Empty;

    /// <summary>Sürücü telefon numarası</summary>
    [Required]
    [Phone]
    [StringLength(20)]
    public string Phone { get; set; } = string.Empty;

    /// <summary>
    /// Sürücünün şifresiz, tek tıkla oturum açmasını sağlayan hızlı erişim kodu (örn: DNZ-101).
    /// </summary>
    [Required]
    [StringLength(20)]
    public string AccessCode { get; set; } = string.Empty;

    /// <summary>Araç plakası</summary>
    [StringLength(20)]
    public string? VehiclePlate { get; set; }

    /// <summary>Sürücü aktif mi?</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Oluşturulma tarihi</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<Order> Orders { get; set; } = [];
}

using System.ComponentModel.DataAnnotations;
using HaliYikamaAPI.Models.Enums;

namespace HaliYikamaAPI.Models;

/// <summary>
/// Halı/Koltuk yıkama siparişi ana modeli.
/// </summary>
public class Order
{
    /// <summary>Benzersiz sipariş kimliği</summary>
    [Key]
    public Guid Id { get; set; }

    /// <summary>Siparişin ait olduğu firma ID'si</summary>
    [Required]
    public Guid CompanyId { get; set; }

    public Company? Company { get; set; }

    /// <summary>Siparişe atanan sürücü ID'si (opsiyonel)</summary>
    public Guid? DriverId { get; set; }

    public Driver? Driver { get; set; }

    /// <summary>Müşteri adı</summary>
    [StringLength(100)]
    public string? CustomerName { get; set; }

    /// <summary>Müşteri telefon numarası</summary>
    [Required]
    [Phone]
    [StringLength(20)]
    public string CustomerPhone { get; set; } = string.Empty;

    /// <summary>Müşteri adresi</summary>
    [Required]
    [StringLength(500)]
    public string CustomerAddress { get; set; } = string.Empty;

    /// <summary>Hizmet türü (Halı veya Koltuk)</summary>
    [Required]
    public ServiceType ServiceType { get; set; }

    /// <summary>Halı adedi (opsiyonel)</summary>
    public int? CarpetCount { get; set; }

    /// <summary>Fiş/makbuz numarası (opsiyonel)</summary>
    [StringLength(50)]
    public string? ReceiptNumber { get; set; }

    /// <summary>Toplam fiyat (opsiyonel)</summary>
    [Range(0, double.MaxValue)]
    public decimal? TotalPrice { get; set; }

    /// <summary>POS cihazı gerekli mi?</summary>
    public bool RequiresPOS { get; set; }

    /// <summary>Sipariş durumu</summary>
    [Required]
    public OrderStatus Status { get; set; } = OrderStatus.YeniTalep;

    /// <summary>Sipariş oluşturulma tarihi</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Son güncelleme tarihi</summary>
    public DateTime? UpdatedAt { get; set; }
}

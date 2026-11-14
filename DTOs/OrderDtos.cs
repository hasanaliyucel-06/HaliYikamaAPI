using System.ComponentModel.DataAnnotations;
using HaliYikamaAPI.Models.Enums;

namespace HaliYikamaAPI.DTOs;

/// <summary>
/// Yeni sipariş oluşturma isteği DTO'su.
/// </summary>
public class CreateOrderDto
{
    /// <summary>Müşteri adı (opsiyonel)</summary>
    [StringLength(100)]
    public string? CustomerName { get; set; }

    /// <summary>Müşteri telefon numarası</summary>
    [Required(ErrorMessage = "Telefon numarası zorunludur.")]
    [Phone(ErrorMessage = "Geçerli bir telefon numarası giriniz.")]
    [StringLength(20)]
    public string CustomerPhone { get; set; } = string.Empty;

    /// <summary>Müşteri adresi</summary>
    [Required(ErrorMessage = "Adres zorunludur.")]
    [StringLength(500)]
    public string CustomerAddress { get; set; } = string.Empty;

    /// <summary>Hizmet türü (0: Hali, 1: Koltuk)</summary>
    [Required(ErrorMessage = "Hizmet türü zorunludur.")]
    public ServiceType ServiceType { get; set; }

    /// <summary>Halı adedi (opsiyonel)</summary>
    public int? CarpetCount { get; set; }

    /// <summary>Fiş/makbuz numarası (opsiyonel)</summary>
    [StringLength(50)]
    public string? ReceiptNumber { get; set; }

    /// <summary>Toplam fiyat (opsiyonel)</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Fiyat sıfırdan küçük olamaz.")]
    public decimal? TotalPrice { get; set; }

    /// <summary>POS cihazı gerekli mi?</summary>
    public bool RequiresPOS { get; set; }
}

/// <summary>
/// Sipariş güncelleme isteği DTO'su.
/// </summary>
public class UpdateOrderDto
{
    /// <summary>Müşteri adı (opsiyonel)</summary>
    [StringLength(100)]
    public string? CustomerName { get; set; }

    /// <summary>Müşteri telefon numarası</summary>
    [Required(ErrorMessage = "Telefon numarası zorunludur.")]
    [Phone(ErrorMessage = "Geçerli bir telefon numarası giriniz.")]
    [StringLength(20)]
    public string CustomerPhone { get; set; } = string.Empty;

    /// <summary>Müşteri adresi</summary>
    [Required(ErrorMessage = "Adres zorunludur.")]
    [StringLength(500)]
    public string CustomerAddress { get; set; } = string.Empty;

    /// <summary>Hizmet türü (0: Hali, 1: Koltuk)</summary>
    [Required(ErrorMessage = "Hizmet türü zorunludur.")]
    public ServiceType ServiceType { get; set; }

    /// <summary>Halı adedi (opsiyonel)</summary>
    public int? CarpetCount { get; set; }

    /// <summary>Fiş/makbuz numarası (opsiyonel)</summary>
    [StringLength(50)]
    public string? ReceiptNumber { get; set; }

    /// <summary>Toplam fiyat (opsiyonel)</summary>
    [Range(0, double.MaxValue, ErrorMessage = "Fiyat sıfırdan küçük olamaz.")]
    public decimal? TotalPrice { get; set; }

    /// <summary>POS cihazı gerekli mi?</summary>
    public bool RequiresPOS { get; set; }

    /// <summary>Sipariş durumu</summary>
    [Required(ErrorMessage = "Sipariş durumu zorunludur.")]
    public OrderStatus Status { get; set; }
}

/// <summary>
/// Sipariş durum güncelleme DTO'su.
/// </summary>
public class UpdateOrderStatusDto
{
    /// <summary>Yeni sipariş durumu</summary>
    [Required(ErrorMessage = "Sipariş durumu zorunludur.")]
    public OrderStatus Status { get; set; }
}

/// <summary>
/// Sipariş yanıt DTO'su.
/// </summary>
public class OrderResponseDto
{
    public Guid Id { get; set; }
    public string? CustomerName { get; set; }
    public string CustomerPhone { get; set; } = string.Empty;
    public string CustomerAddress { get; set; } = string.Empty;
    public ServiceType ServiceType { get; set; }
    public int? CarpetCount { get; set; }
    public string? ReceiptNumber { get; set; }
    public decimal? TotalPrice { get; set; }
    public bool RequiresPOS { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

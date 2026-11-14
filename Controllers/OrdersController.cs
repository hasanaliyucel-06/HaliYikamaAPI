using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaliYikamaAPI.Data;
using HaliYikamaAPI.Models;
using HaliYikamaAPI.Models.Enums;
using HaliYikamaAPI.DTOs;
using HaliYikamaAPI.Services.WhatsApp;

namespace HaliYikamaAPI.Controllers;

/// <summary>
/// Halı/Koltuk yıkama siparişleri CRUD API kontrolcüsü.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class OrdersController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IWhatsAppSender _whatsAppSender;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        ApplicationDbContext context,
        IWhatsAppSender whatsAppSender,
        ILogger<OrdersController> logger)
    {
        _context = context;
        _whatsAppSender = whatsAppSender;
        _logger = logger;
    }

    // ──────────────────────────────────────────────
    //  GET: api/orders
    // ──────────────────────────────────────────────

    /// <summary>
    /// Tüm siparişleri listeler. Opsiyonel olarak duruma ve sayfalama parametrelerine göre filtreleme yapılabilir.
    /// </summary>
    /// <param name="status">Sipariş durumu filtresi (opsiyonel)</param>
    /// <param name="page">Sayfa numarası (1'den başlar, opsiyonel)</param>
    /// <param name="pageSize">Sayfa başına kayıt sayısı (opsiyonel)</param>
    /// <returns>Sipariş listesi</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<OrderResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<OrderResponseDto>>> GetOrders(
        [FromQuery] OrderStatus? status = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        _logger.LogInformation("Siparişler listeleniyor. Filtre: {Status}", status?.ToString() ?? "Yok");

        IQueryable<Order> query = _context.Orders.AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        query = query.OrderByDescending(o => o.CreatedAt);

        if (page.HasValue && pageSize.HasValue && page.Value > 0 && pageSize.Value > 0)
        {
            query = query.Skip((page.Value - 1) * pageSize.Value).Take(pageSize.Value);
        }

        var orders = await query
            .Select(o => MapToResponseDto(o))
            .ToListAsync();

        return Ok(orders);
    }

    // ──────────────────────────────────────────────
    //  GET: api/orders/{id}
    // ──────────────────────────────────────────────

    /// <summary>
    /// Belirtilen ID'ye sahip siparişi getirir.
    /// </summary>
    /// <param name="id">Sipariş ID'si</param>
    /// <returns>Sipariş detayı</returns>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponseDto>> GetOrder(Guid id)
    {
        _logger.LogInformation("Sipariş getiriliyor: {OrderId}", id);

        var order = await _context.Orders
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
        {
            _logger.LogWarning("Sipariş bulunamadı: {OrderId}", id);
            return NotFound(new { message = $"Sipariş bulunamadı: {id}" });
        }

        return Ok(MapToResponseDto(order));
    }

    // ──────────────────────────────────────────────
    //  POST: api/orders
    // ──────────────────────────────────────────────

    /// <summary>
    /// Yeni bir sipariş oluşturur.
    /// </summary>
    /// <param name="dto">Sipariş oluşturma verileri</param>
    /// <returns>Oluşturulan sipariş</returns>
    [HttpPost]
    [ProducesResponseType(typeof(OrderResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderResponseDto>> CreateOrder([FromBody] CreateOrderDto dto)
    {
        _logger.LogInformation("Yeni sipariş oluşturuluyor: {Phone}, {ServiceType}",
            dto.CustomerPhone, dto.ServiceType);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerName = dto.CustomerName,
            CustomerPhone = dto.CustomerPhone,
            CustomerAddress = dto.CustomerAddress,
            ServiceType = dto.ServiceType,
            CarpetCount = dto.CarpetCount,
            ReceiptNumber = dto.ReceiptNumber,
            TotalPrice = dto.TotalPrice,
            RequiresPOS = dto.RequiresPOS,
            Status = OrderStatus.YeniTalep,
            CreatedAt = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Sipariş oluşturuldu: {OrderId}", order.Id);

        var responseDto = MapToResponseDto(order);

        return CreatedAtAction(
            nameof(GetOrder),
            new { id = order.Id },
            responseDto);
    }

    // ──────────────────────────────────────────────
    //  PUT: api/orders/{id}
    // ──────────────────────────────────────────────

    /// <summary>
    /// Belirtilen siparişi tamamen günceller.
    /// </summary>
    /// <param name="id">Sipariş ID'si</param>
    /// <param name="dto">Güncelleme verileri</param>
    /// <returns>Güncellenmiş sipariş</returns>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(OrderResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderResponseDto>> UpdateOrder(Guid id, [FromBody] UpdateOrderDto dto)
    {
        _logger.LogInformation("Sipariş güncelleniyor: {OrderId}", id);

        var order = await _context.Orders.FindAsync(id);

        if (order is null)
        {
            _logger.LogWarning("Güncellenecek sipariş bulunamadı: {OrderId}", id);
            return NotFound(new { message = $"Sipariş bulunamadı: {id}" });
        }

        order.CustomerName = dto.CustomerName;
        order.CustomerPhone = dto.CustomerPhone;
        order.CustomerAddress = dto.CustomerAddress;
        order.ServiceType = dto.ServiceType;
        order.CarpetCount = dto.CarpetCount;
        order.ReceiptNumber = dto.ReceiptNumber;
        order.TotalPrice = dto.TotalPrice;
        order.RequiresPOS = dto.RequiresPOS;
        order.Status = dto.Status;
        order.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        _logger.LogInformation("Sipariş güncellendi: {OrderId}", order.Id);

        return Ok(MapToResponseDto(order));
    }

    // ──────────────────────────────────────────────
    //  PATCH: api/orders/{id}/status
    // ──────────────────────────────────────────────

    /// <summary>
    /// Siparişin durumunu günceller.
    /// </summary>
    /// <param name="id">Sipariş ID'si</param>
    /// <param name="dto">Yeni durum</param>
    /// <returns>Güncellenmiş sipariş</returns>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(OrderResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderResponseDto>> UpdateOrderStatus(
        Guid id, [FromBody] UpdateOrderStatusDto dto)
    {
        _logger.LogInformation("Sipariş durumu güncelleniyor: {OrderId} -> {Status}", id, dto.Status);

        var order = await _context.Orders.FindAsync(id);

        if (order is null)
        {
            _logger.LogWarning("Durumu güncellenecek sipariş bulunamadı: {OrderId}", id);
            return NotFound(new { message = $"Sipariş bulunamadı: {id}" });
        }

        order.Status = dto.Status;
        order.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Sipariş durumu güncellendi: {OrderId} -> {Status}", order.Id, order.Status);

        // Müşteriye WhatsApp durum bildirimi gönder (arka planda, hata ana akışı engellemez)
        _ = TrySendStatusNotificationAsync(order, dto.Status);

        return Ok(MapToResponseDto(order));
    }

    // ──────────────────────────────────────────────
    //  DELETE: api/orders/{id}
    // ──────────────────────────────────────────────

    /// <summary>
    /// Belirtilen siparişi siler.
    /// </summary>
    /// <param name="id">Sipariş ID'si</param>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteOrder(Guid id)
    {
        _logger.LogInformation("Sipariş siliniyor: {OrderId}", id);

        var order = await _context.Orders.FindAsync(id);

        if (order is null)
        {
            _logger.LogWarning("Silinecek sipariş bulunamadı: {OrderId}", id);
            return NotFound(new { message = $"Sipariş bulunamadı: {id}" });
        }

        _context.Orders.Remove(order);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Sipariş silindi: {OrderId}", id);

        return NoContent();
    }

    // ──────────────────────────────────────────────
    //  GET: api/orders/search?phone=...
    // ──────────────────────────────────────────────

    /// <summary>
    /// Telefon numarasına veya müşteri adına göre siparişleri arar.
    /// </summary>
    /// <param name="phone">Aranacak telefon numarası veya isim (kısmi eşleşme)</param>
    /// <param name="query">Genel arama sorgusu (telefon veya isim)</param>
    /// <returns>Eşleşen siparişler</returns>
    [HttpGet("search")]
    [ProducesResponseType(typeof(IEnumerable<OrderResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<OrderResponseDto>>> SearchByPhone(
        [FromQuery] string? phone = null,
        [FromQuery] string? query = null)
    {
        var searchTerm = query ?? phone;
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return BadRequest(new { message = "Arama terimi boş olamaz." });
        }

        _logger.LogInformation("Sipariş arama: {SearchTerm}", searchTerm);

        var orders = await _context.Orders
            .AsNoTracking()
            .Where(o => o.CustomerPhone.Contains(searchTerm) || (o.CustomerName != null && o.CustomerName.Contains(searchTerm)))
            .OrderByDescending(o => o.CreatedAt)
            .Take(50)
            .Select(o => MapToResponseDto(o))
            .ToListAsync();

        return Ok(orders);
    }

    // ──────────────────────────────────────────────
    //  Mapping Helper
    // ──────────────────────────────────────────────

    private static OrderResponseDto MapToResponseDto(Order order) => new()
    {
        Id = order.Id,
        CustomerName = order.CustomerName,
        CustomerPhone = order.CustomerPhone,
        CustomerAddress = order.CustomerAddress,
        ServiceType = order.ServiceType,
        CarpetCount = order.CarpetCount,
        ReceiptNumber = order.ReceiptNumber,
        TotalPrice = order.TotalPrice,
        RequiresPOS = order.RequiresPOS,
        Status = order.Status,
        CreatedAt = order.CreatedAt,
        UpdatedAt = order.UpdatedAt
    };

    private async Task TrySendStatusNotificationAsync(Order order, OrderStatus newStatus)
    {
        string? message = newStatus switch
        {
            OrderStatus.Hazir => "Sayın müşterimiz, siparişiniz yıkanmış ve paketlenmiş olup teslime hazır durumdadır. 🧼✨",
            OrderStatus.Dagitimda => "Sayın müşterimiz, siparişiniz dağıtıma çıkmıştır. Ekibimiz en kısa sürede adresinize teslim edecektir. 🚚",
            OrderStatus.Tamamlandi => "Sayın müşterimiz, siparişiniz tamamlanmıştır. Bizi tercih ettiğiniz için teşekkür ederiz! 🙏",
            OrderStatus.IptalEdildi => "Sayın müşterimiz, siparişiniz iptal edilmiştir. Bilgi almak için bizimle iletişime geçebilirsiniz.",
            _ => null
        };

        if (message == null) return;

        try
        {
            var waPhone = FormatPhoneForWhatsApp(order.CustomerPhone);
            await _whatsAppSender.SendTextMessageAsync(waPhone, message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WhatsApp durum bildirimi gönderilemedi: {Phone}", order.CustomerPhone);
        }
    }

    private static string FormatPhoneForWhatsApp(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("0") && digits.Length == 11)
        {
            return "90" + digits[1..];
        }
        if (digits.Length == 10)
        {
            return "90" + digits;
        }
        return digits;
    }
}

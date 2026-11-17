using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaliYikamaAPI.Data;
using HaliYikamaAPI.Models;
using HaliYikamaAPI.Models.Enums;
using HaliYikamaAPI.DTOs;
using HaliYikamaAPI.Services.WhatsApp;
using HaliYikamaAPI.Services.Auth;

namespace HaliYikamaAPI.Controllers;

/// <summary>
/// Halı/Koltuk yıkama siparişleri CRUD API kontrolcüsü. Çoklu firma (multi-tenant) ve sürücü desteğine sahiptir.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class OrdersController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        ApplicationDbContext context,
        ICurrentTenantService tenantService,
        IServiceScopeFactory scopeFactory,
        ILogger<OrdersController> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    // ──────────────────────────────────────────────
    //  GET: api/orders
    // ──────────────────────────────────────────────

    /// <summary>
    /// Firmanın siparişlerini listeler. Durum, sürücü ve sayfalama parametrelerine göre filtrelenebilir.
    /// </summary>
    /// <param name="status">Sipariş durumu filtresi (opsiyonel)</param>
    /// <param name="driverId">Belirli bir sürücüye atanmış siparişler (opsiyonel)</param>
    /// <param name="unassignedOnly">Sadece henüz sürücü atanmamış siparişleri getir</param>
    /// <param name="activeOnly">Yalnızca tamamlanmamış ve iptal edilmemiş aktif siparişleri getir</param>
    /// <param name="page">Sayfa numarası (1'den başlar, opsiyonel)</param>
    /// <param name="pageSize">Sayfa başına kayıt sayısı (opsiyonel)</param>
    /// <returns>Sipariş listesi</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<OrderResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<OrderResponseDto>>> GetOrders(
        [FromQuery] OrderStatus? status = null,
        [FromQuery] Guid? driverId = null,
        [FromQuery] bool? unassignedOnly = null,
        [FromQuery] bool? activeOnly = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null)
    {
        var companyId = _tenantService.CompanyId;
        _logger.LogInformation("Siparişler listeleniyor. Firma: {CompanyId}, Filtre: {Status}",
            companyId, status?.ToString() ?? "Yok");

        IQueryable<Order> query = _context.Orders
            .AsNoTracking()
            .Where(o => o.CompanyId == companyId);

        // Rol kontrolü: Sürücü yalnızca kendisine atanmış siparişleri görebilir
        if (_tenantService.IsDriver && _tenantService.DriverId.HasValue)
        {
            query = query.Where(o => o.DriverId == _tenantService.DriverId.Value);
        }
        else
        {
            // Firma yöneticisi filtreleri
            if (driverId.HasValue)
            {
                query = query.Where(o => o.DriverId == driverId.Value);
            }
            else if (unassignedOnly == true)
            {
                query = query.Where(o => o.DriverId == null);
            }
        }

        if (activeOnly == true)
        {
            query = query.Where(o => o.Status != OrderStatus.Tamamlandi && o.Status != OrderStatus.IptalEdildi);
        }

        if (status.HasValue)
        {
            query = query.Where(o => o.Status == status.Value);
        }

        var totalCount = await query.CountAsync();
        Response.Headers["X-Total-Count"] = totalCount.ToString();

        query = query.OrderByDescending(o => o.CreatedAt);

        if (page.HasValue && pageSize.HasValue && page.Value > 0 && pageSize.Value > 0)
        {
            var clampedSize = Math.Min(pageSize.Value, 100);
            query = query.Skip((page.Value - 1) * clampedSize).Take(clampedSize);
        }

        var orders = await query
            .Select(o => new OrderResponseDto
            {
                Id = o.Id,
                CompanyId = o.CompanyId,
                DriverId = o.DriverId,
                DriverName = o.Driver != null ? o.Driver.FullName : null,
                CustomerName = o.CustomerName,
                CustomerPhone = o.CustomerPhone,
                CustomerAddress = o.CustomerAddress,
                ServiceType = o.ServiceType,
                CarpetCount = o.CarpetCount,
                ReceiptNumber = o.ReceiptNumber,
                TotalPrice = o.TotalPrice,
                RequiresPOS = o.RequiresPOS,
                Status = o.Status,
                CreatedAt = o.CreatedAt,
                UpdatedAt = o.UpdatedAt
            })
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
        var companyId = _tenantService.CompanyId;
        _logger.LogInformation("Sipariş getiriliyor: {OrderId}, Firma: {CompanyId}", id, companyId);

        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Driver)
            .FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId);

        if (order is null)
        {
            _logger.LogWarning("Sipariş bulunamadı: {OrderId}", id);
            return NotFound(new { message = $"Sipariş bulunamadı: {id}" });
        }

        // Sürücü yalnızca kendisine atanmış siparişi görüntüleyebilir
        if (_tenantService.IsDriver && order.DriverId != _tenantService.DriverId)
        {
            return Forbid();
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
    [Authorize(Roles = "CompanyAdmin")]
    [ProducesResponseType(typeof(OrderResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderResponseDto>> CreateOrder([FromBody] CreateOrderDto dto)
    {
        var companyId = _tenantService.CompanyId;
        _logger.LogInformation("Yeni sipariş oluşturuluyor: Firma {CompanyId}, {Phone}, {ServiceType}",
            companyId, dto.CustomerPhone, dto.ServiceType);

        if (dto.DriverId.HasValue)
        {
            var driverExists = await _context.Drivers
                .AnyAsync(d => d.Id == dto.DriverId.Value && d.CompanyId == companyId && d.IsActive);
            if (!driverExists)
            {
                return BadRequest(new { message = "Seçilen sürücü aktif değil, bu firmaya ait değil veya bulunamadı." });
            }
        }

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            DriverId = dto.DriverId,
            CustomerName = string.IsNullOrWhiteSpace(dto.CustomerName) ? null : dto.CustomerName.Trim(),
            CustomerPhone = dto.CustomerPhone.Trim(),
            CustomerAddress = dto.CustomerAddress.Trim(),
            ServiceType = dto.ServiceType,
            CarpetCount = dto.ServiceType == ServiceType.Koltuk ? null : dto.CarpetCount,
            ReceiptNumber = string.IsNullOrWhiteSpace(dto.ReceiptNumber) ? null : dto.ReceiptNumber.Trim(),
            TotalPrice = dto.TotalPrice,
            RequiresPOS = dto.RequiresPOS,
            Status = OrderStatus.YeniTalep,
            CreatedAt = DateTime.UtcNow
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        if (order.DriverId.HasValue)
        {
            await _context.Entry(order).Reference(o => o.Driver).LoadAsync();
        }

        _logger.LogInformation("Sipariş oluşturuldu: {OrderId} (Firma: {CompanyId})", order.Id, companyId);

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
    [Authorize(Roles = "CompanyAdmin")]
    [ProducesResponseType(typeof(OrderResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderResponseDto>> UpdateOrder(Guid id, [FromBody] UpdateOrderDto dto)
    {
        var companyId = _tenantService.CompanyId;
        _logger.LogInformation("Sipariş güncelleniyor: {OrderId} (Firma: {CompanyId})", id, companyId);

        var order = await _context.Orders
            .Include(o => o.Driver)
            .FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId);

        if (order is null)
        {
            _logger.LogWarning("Güncellenecek sipariş bulunamadı: {OrderId}", id);
            return NotFound(new { message = $"Sipariş bulunamadı: {id}" });
        }

        if (order.Status == OrderStatus.Tamamlandi || order.Status == OrderStatus.IptalEdildi)
        {
            return BadRequest(new { message = $"'{order.Status}' durumundaki kilitli sipariş üzerinde değişiklik yapılamaz." });
        }

        if (dto.DriverId.HasValue && dto.DriverId != order.DriverId)
        {
            var driverExists = await _context.Drivers
                .AnyAsync(d => d.Id == dto.DriverId.Value && d.CompanyId == companyId && d.IsActive);
            if (!driverExists)
            {
                return BadRequest(new { message = "Seçilen sürücü aktif değil, bu firmaya ait değil veya bulunamadı." });
            }
        }

        var previousStatus = order.Status;

        order.CustomerName = string.IsNullOrWhiteSpace(dto.CustomerName) ? null : dto.CustomerName.Trim();
        order.CustomerPhone = dto.CustomerPhone.Trim();
        order.CustomerAddress = dto.CustomerAddress.Trim();
        order.ServiceType = dto.ServiceType;
        order.CarpetCount = dto.ServiceType == ServiceType.Koltuk ? null : dto.CarpetCount;
        order.ReceiptNumber = string.IsNullOrWhiteSpace(dto.ReceiptNumber) ? null : dto.ReceiptNumber.Trim();
        order.TotalPrice = dto.TotalPrice;
        order.RequiresPOS = dto.RequiresPOS;
        order.Status = dto.Status;
        order.DriverId = dto.DriverId;
        order.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        if (order.DriverId.HasValue)
        {
            await _context.Entry(order).Reference(o => o.Driver).LoadAsync();
        }
        else
        {
            order.Driver = null;
        }

        _logger.LogInformation("Sipariş güncellendi: {OrderId}", order.Id);

        if (previousStatus != order.Status)
        {
            QueueStatusNotification(order.CompanyId, order.Id, order.CustomerPhone, order.Status);
        }

        return Ok(MapToResponseDto(order));
    }

    // ──────────────────────────────────────────────
    //  PATCH: api/orders/{id}/status
    // ──────────────────────────────────────────────

    /// <summary>
    /// Siparişin durumunu günceller. Sürücüler ve firma yöneticileri kullanabilir.
    /// </summary>
    /// <param name="id">Sipariş ID'si</param>
    /// <param name="dto">Yeni durum</param>
    /// <returns>Güncellenmiş sipariş</returns>
    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(OrderResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderResponseDto>> UpdateOrderStatus(
        Guid id, [FromBody] UpdateOrderStatusDto dto)
    {
        var companyId = _tenantService.CompanyId;
        _logger.LogInformation("Sipariş durumu güncelleniyor: {OrderId} -> {Status}", id, dto.Status);

        var order = await _context.Orders
            .Include(o => o.Driver)
            .FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId);

        if (order is null)
        {
            _logger.LogWarning("Durumu güncellenecek sipariş bulunamadı: {OrderId}", id);
            return NotFound(new { message = $"Sipariş bulunamadı: {id}" });
        }

        // Sürücü rolü kontrolü: Sürücü yalnızca kendisine atanan siparişin durumunu güncelleyebilir
        if (_tenantService.IsDriver && order.DriverId != _tenantService.DriverId)
        {
            return Forbid();
        }

        // Durum geçiş kontrolü (Örn: Tamamlanmış veya İptal edilmiş sipariş değiştirilemez)
        if (!IsValidStatusTransition(order.Status, dto.Status, order.ServiceType))
        {
            return BadRequest(new { message = $"'{order.Status}' durumundan '{dto.Status}' durumuna doğrudan geçiş yapılamaz." });
        }

        var previousStatus = order.Status;
        order.Status = dto.Status;
        order.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Sipariş durumu güncellendi: {OrderId} -> {Status}", order.Id, order.Status);

        // Müşteriye WhatsApp durum bildirimi gönder (yeni DI scope içinde güvenli arka plan yürütme)
        if (previousStatus != order.Status)
        {
            QueueStatusNotification(order.CompanyId, order.Id, order.CustomerPhone, order.Status);
        }

        return Ok(MapToResponseDto(order));
    }

    // ──────────────────────────────────────────────
    //  PATCH: api/orders/{id}/assign-driver
    // ──────────────────────────────────────────────

    /// <summary>
    /// Siparişe sürücü atar veya atamayı kaldırır (DriverId null gönderilirse kaldırılır). Yalnızca firma yöneticisi yapabilir.
    /// </summary>
    /// <param name="id">Sipariş ID'si</param>
    /// <param name="dto">Sürücü atama verisi</param>
    /// <returns>Güncellenmiş sipariş</returns>
    [HttpPatch("{id:guid}/assign-driver")]
    [Authorize(Roles = "CompanyAdmin")]
    [ProducesResponseType(typeof(OrderResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderResponseDto>> AssignDriver(
        Guid id, [FromBody] AssignDriverDto dto)
    {
        var companyId = _tenantService.CompanyId;
        _logger.LogInformation("Siparişe sürücü atanıyor: {OrderId} -> Sürücü: {DriverId}", id, dto.DriverId);

        var order = await _context.Orders
            .Include(o => o.Driver)
            .FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId);

        if (order is null)
        {
            return NotFound(new { message = $"Sipariş bulunamadı: {id}" });
        }

        if (order.Status == OrderStatus.Tamamlandi || order.Status == OrderStatus.IptalEdildi)
        {
            return BadRequest(new { message = $"'{order.Status}' durumundaki kilitli siparişte sürücü değiştirilemez." });
        }

        if (dto.DriverId.HasValue)
        {
            var driver = await _context.Drivers
                .FirstOrDefaultAsync(d => d.Id == dto.DriverId.Value && d.CompanyId == companyId && d.IsActive);

            if (driver is null)
            {
                return BadRequest(new { message = "Belirtilen sürücü aktif değil, bulunamadı veya bu firmaya ait değil." });
            }

            order.DriverId = driver.Id;
            order.Driver = driver;
        }
        else
        {
            order.DriverId = null;
            order.Driver = null;
        }

        order.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Sürücü ataması başarılı: {OrderId} -> {DriverName}",
            order.Id, order.Driver?.FullName ?? "Atama Kaldırıldı");

        return Ok(MapToResponseDto(order));
    }

    // ──────────────────────────────────────────────
    //  DELETE: api/orders/{id}
    // ──────────────────────────────────────────────

    /// <summary>
    /// Belirtilen siparişi siler. Yalnızca firma yöneticileri silebilir.
    /// </summary>
    /// <param name="id">Sipariş ID'si</param>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "CompanyAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteOrder(Guid id)
    {
        var companyId = _tenantService.CompanyId;
        _logger.LogInformation("Sipariş siliniyor: {OrderId} (Firma: {CompanyId})", id, companyId);

        var order = await _context.Orders
            .FirstOrDefaultAsync(o => o.Id == id && o.CompanyId == companyId);

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
    /// Telefon numarasına veya müşteri adına göre firmanın siparişlerini arar.
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
        var searchTerm = (query ?? phone)?.Trim();
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return BadRequest(new { message = "Arama terimi boş olamaz." });
        }

        var companyId = _tenantService.CompanyId;
        _logger.LogInformation("Sipariş arama (Firma: {CompanyId}): {SearchTerm}", companyId, searchTerm);

        IQueryable<Order> searchQuery = _context.Orders
            .AsNoTracking()
            .Where(o => o.CompanyId == companyId &&
                        (o.CustomerPhone.Contains(searchTerm) || (o.CustomerName != null && o.CustomerName.Contains(searchTerm))));

        if (_tenantService.IsDriver && _tenantService.DriverId.HasValue)
        {
            searchQuery = searchQuery.Where(o => o.DriverId == _tenantService.DriverId.Value);
        }

        var orders = await searchQuery
            .OrderByDescending(o => o.CreatedAt)
            .Take(50)
            .Select(o => new OrderResponseDto
            {
                Id = o.Id,
                CompanyId = o.CompanyId,
                DriverId = o.DriverId,
                DriverName = o.Driver != null ? o.Driver.FullName : null,
                CustomerName = o.CustomerName,
                CustomerPhone = o.CustomerPhone,
                CustomerAddress = o.CustomerAddress,
                ServiceType = o.ServiceType,
                CarpetCount = o.CarpetCount,
                ReceiptNumber = o.ReceiptNumber,
                TotalPrice = o.TotalPrice,
                RequiresPOS = o.RequiresPOS,
                Status = o.Status,
                CreatedAt = o.CreatedAt,
                UpdatedAt = o.UpdatedAt
            })
            .ToListAsync();

        return Ok(orders);
    }

    // ──────────────────────────────────────────────
    //  Mapping Helper
    // ──────────────────────────────────────────────

    private static OrderResponseDto MapToResponseDto(Order order) => new()
    {
        Id = order.Id,
        CompanyId = order.CompanyId,
        DriverId = order.DriverId,
        DriverName = order.Driver?.FullName,
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

    private void QueueStatusNotification(Guid companyId, Guid orderId, string customerPhone, OrderStatus newStatus)
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

        var waPhone = FormatPhoneForWhatsApp(customerPhone);
        var scopeFactory = _scopeFactory;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var phoneNumberId = await db.Companies
                    .AsNoTracking()
                    .Where(c => c.Id == companyId)
                    .Select(c => c.WhatsAppPhoneNumberId)
                    .FirstOrDefaultAsync();

                var sender = scope.ServiceProvider.GetRequiredService<IWhatsAppSender>();
                await sender.SendTextMessageAsync(waPhone, message, phoneNumberId);
            }
            catch (Exception ex)
            {
                try
                {
                    using var logScope = scopeFactory.CreateScope();
                    var logger = logScope.ServiceProvider.GetRequiredService<ILogger<OrdersController>>();
                    logger.LogWarning(ex, "WhatsApp durum bildirimi gönderilemedi: {OrderId} ({Phone})", orderId, customerPhone);
                }
                catch
                {
                    // Loglama hatası sessizce geçilir
                }
            }
        });
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

    /// <summary>
    /// Durum geçiş kurallarını kontrol eder.
    /// </summary>
    private static bool IsValidStatusTransition(OrderStatus current, OrderStatus next, ServiceType service)
    {
        if (current == next) return true;

        // Tamamlanmış ve İptal edilmiş siparişler kilitlidir
        if (current == OrderStatus.IptalEdildi || current == OrderStatus.Tamamlandi)
        {
            return false;
        }

        // İstisna: Her aktif durumdan doğrudan İptal Edildi'ye geçilebilir
        if (next == OrderStatus.IptalEdildi) return true;

        return current switch
        {
            OrderStatus.YeniTalep => next == OrderStatus.Alinacak || next == OrderStatus.Islemde,
            OrderStatus.Alinacak => next == OrderStatus.Islemde || (service == ServiceType.Koltuk && next == OrderStatus.Tamamlandi),
            OrderStatus.Islemde => next == OrderStatus.Hazir,
            OrderStatus.Hazir => next == OrderStatus.Dagitimda,
            OrderStatus.Dagitimda => next == OrderStatus.Tamamlandi,
            _ => false
        };
    }
}

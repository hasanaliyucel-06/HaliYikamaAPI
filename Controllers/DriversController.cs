using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaliYikamaAPI.Data;
using HaliYikamaAPI.DTOs;
using HaliYikamaAPI.Models;
using HaliYikamaAPI.Models.Enums;
using HaliYikamaAPI.Services.Auth;

namespace HaliYikamaAPI.Controllers;

/// <summary>
/// Sürücü yönetimi CRUD API kontrolcüsü.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DriversController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ILogger<DriversController> _logger;

    public DriversController(
        ApplicationDbContext context,
        ICurrentTenantService tenantService,
        ILogger<DriversController> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _logger = logger;
    }

    /// <summary>
    /// Firmanın tüm sürücülerini listeler.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<DriverResponseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<DriverResponseDto>>> GetDrivers()
    {
        var companyId = _tenantService.CompanyId;

        var drivers = await _context.Drivers
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DriverResponseDto
            {
                Id = d.Id,
                CompanyId = d.CompanyId,
                FullName = d.FullName,
                Phone = d.Phone,
                AccessCode = d.AccessCode,
                VehiclePlate = d.VehiclePlate,
                IsActive = d.IsActive,
                ActiveOrderCount = d.Orders.Count(o => o.Status != OrderStatus.Tamamlandi && o.Status != OrderStatus.IptalEdildi),
                CreatedAt = d.CreatedAt
            })
            .ToListAsync();

        return Ok(drivers);
    }

    /// <summary>
    /// Belirtilen ID'ye sahip sürücüyü getirir.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DriverResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverResponseDto>> GetDriver(Guid id)
    {
        var companyId = _tenantService.CompanyId;

        var driver = await _context.Drivers
            .AsNoTracking()
            .Where(d => d.CompanyId == companyId && d.Id == id)
            .Select(d => new DriverResponseDto
            {
                Id = d.Id,
                CompanyId = d.CompanyId,
                FullName = d.FullName,
                Phone = d.Phone,
                AccessCode = d.AccessCode,
                VehiclePlate = d.VehiclePlate,
                IsActive = d.IsActive,
                ActiveOrderCount = d.Orders.Count(o => o.Status != OrderStatus.Tamamlandi && o.Status != OrderStatus.IptalEdildi),
                CreatedAt = d.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (driver == null)
            return NotFound(new { message = "Sürücü bulunamadı." });

        return Ok(driver);
    }

    /// <summary>
    /// Yeni sürücü ekler. Erişim kodu belirtilmezse otomatik üretilir.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(DriverResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DriverResponseDto>> CreateDriver([FromBody] CreateDriverDto dto)
    {
        var companyId = _tenantService.CompanyId;
        var company = await _context.Companies.FindAsync(companyId);
        if (company == null)
            return BadRequest(new { message = "Geçersiz firma." });

        string accessCode;
        if (!string.IsNullOrWhiteSpace(dto.AccessCode))
        {
            accessCode = dto.AccessCode.Trim().ToUpperInvariant();
            var exists = await _context.Drivers.AnyAsync(d => d.AccessCode.ToUpper() == accessCode);
            if (exists)
                return BadRequest(new { message = $"'{accessCode}' erişim kodu zaten başka bir sürücü tarafından kullanılıyor." });
        }
        else
        {
            // Otomatik kod üret: Örn: DNZ-101, DNZ-102
            accessCode = await GenerateUniqueAccessCodeAsync(company.CodePrefix);
        }

        var driver = new Driver
        {
            Id = Guid.NewGuid(),
            CompanyId = companyId,
            FullName = dto.FullName.Trim(),
            Phone = dto.Phone.Trim(),
            AccessCode = accessCode,
            VehiclePlate = dto.VehiclePlate?.Trim(),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Drivers.Add(driver);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Yeni sürücü oluşturuldu: {Name} (Kod: {Code})", driver.FullName, driver.AccessCode);

        var response = new DriverResponseDto
        {
            Id = driver.Id,
            CompanyId = driver.CompanyId,
            FullName = driver.FullName,
            Phone = driver.Phone,
            AccessCode = driver.AccessCode,
            VehiclePlate = driver.VehiclePlate,
            IsActive = driver.IsActive,
            ActiveOrderCount = 0,
            CreatedAt = driver.CreatedAt
        };

        return CreatedAtAction(nameof(GetDriver), new { id = driver.Id }, response);
    }

    /// <summary>
    /// Sürücü bilgilerini günceller.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(DriverResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriverResponseDto>> UpdateDriver(Guid id, [FromBody] UpdateDriverDto dto)
    {
        var companyId = _tenantService.CompanyId;
        var driver = await _context.Drivers.FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId);

        if (driver == null)
            return NotFound(new { message = "Sürücü bulunamadı." });

        driver.FullName = dto.FullName.Trim();
        driver.Phone = dto.Phone.Trim();
        driver.VehiclePlate = dto.VehiclePlate?.Trim();
        driver.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Sürücü güncellendi: {Name}", driver.FullName);

        return Ok(new DriverResponseDto
        {
            Id = driver.Id,
            CompanyId = driver.CompanyId,
            FullName = driver.FullName,
            Phone = driver.Phone,
            AccessCode = driver.AccessCode,
            VehiclePlate = driver.VehiclePlate,
            IsActive = driver.IsActive,
            ActiveOrderCount = await _context.Orders.CountAsync(o => o.DriverId == driver.Id && o.Status != OrderStatus.Tamamlandi && o.Status != OrderStatus.IptalEdildi),
            CreatedAt = driver.CreatedAt
        });
    }

    /// <summary>
    /// Sürücüyü pasife alır.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteDriver(Guid id)
    {
        var companyId = _tenantService.CompanyId;
        var driver = await _context.Drivers.FirstOrDefaultAsync(d => d.Id == id && d.CompanyId == companyId);

        if (driver == null)
            return NotFound(new { message = "Sürücü bulunamadı." });

        driver.IsActive = false;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private async Task<string> GenerateUniqueAccessCodeAsync(string prefix)
    {
        for (int i = 101; i < 9999; i++)
        {
            var code = $"{prefix}-{i}";
            if (!await _context.Drivers.AnyAsync(d => d.AccessCode == code))
            {
                return code;
            }
        }
        return $"{prefix}-{Random.Shared.Next(1000, 9999)}";
    }
}

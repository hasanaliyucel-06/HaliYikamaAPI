using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using HaliYikamaAPI.Data;
using HaliYikamaAPI.DTOs;
using HaliYikamaAPI.Services.Auth;

namespace HaliYikamaAPI.Controllers;

/// <summary>
/// Firma profili, WhatsApp entegrasyon ayarları ve şifre yönetimi API'si.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "CompanyAdmin")]
[Produces("application/json")]
public class CompanyController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ILogger<CompanyController> _logger;

    public CompanyController(
        ApplicationDbContext context,
        ICurrentTenantService tenantService,
        ILogger<CompanyController> logger)
    {
        _context = context;
        _tenantService = tenantService;
        _logger = logger;
    }

    /// <summary>
    /// Oturum açmış firmanın profil ve ayar bilgilerini getirir.
    /// </summary>
    [HttpGet("profile")]
    [ProducesResponseType(typeof(CompanyProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompanyProfileDto>> GetProfile()
    {
        var company = await _context.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == _tenantService.CompanyId);

        if (company is null)
            return NotFound(new { message = "Firma bulunamadı." });

        return Ok(MapToProfileDto(company));
    }

    /// <summary>
    /// Firma bilgilerini ve WhatsApp PhoneNumberId ayarını günceller.
    /// </summary>
    [HttpPut("profile")]
    [ProducesResponseType(typeof(CompanyProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompanyProfileDto>> UpdateProfile([FromBody] UpdateCompanyProfileDto dto)
    {
        var company = await _context.Companies
            .FirstOrDefaultAsync(c => c.Id == _tenantService.CompanyId);

        if (company is null)
            return NotFound(new { message = "Firma bulunamadı." });

        var normalizedWaId = string.IsNullOrWhiteSpace(dto.WhatsAppPhoneNumberId)
            ? null
            : dto.WhatsAppPhoneNumberId.Trim();

        if (normalizedWaId != null && normalizedWaId != company.WhatsAppPhoneNumberId)
        {
            var inUse = await _context.Companies
                .AnyAsync(c => c.Id != company.Id && c.WhatsAppPhoneNumberId == normalizedWaId);
            if (inUse)
            {
                return BadRequest(new { message = "Bu WhatsApp Phone Number ID başka bir firma tarafından kullanılıyor." });
            }
        }

        company.Name = dto.Name.Trim();
        company.Phone = dto.Phone.Trim();
        company.Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim();
        company.WhatsAppPhoneNumberId = normalizedWaId;

        await _context.SaveChangesAsync();
        _logger.LogInformation("Firma profili güncellendi: {CompanyName} ({CompanyId})", company.Name, company.Id);

        return Ok(MapToProfileDto(company));
    }

    /// <summary>
    /// Firma yönetici şifresini değiştirir.
    /// </summary>
    [HttpPost("change-password")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var company = await _context.Companies
            .FirstOrDefaultAsync(c => c.Id == _tenantService.CompanyId);

        if (company is null)
            return NotFound(new { message = "Firma bulunamadı." });

        if (!PasswordHasher.VerifyPassword(dto.CurrentPassword, company.PasswordHash))
        {
            return BadRequest(new { message = "Mevcut şifre hatalı." });
        }

        if (dto.CurrentPassword == dto.NewPassword)
        {
            return BadRequest(new { message = "Yeni şifre mevcut şifre ile aynı olamaz." });
        }

        company.PasswordHash = PasswordHasher.HashPassword(dto.NewPassword);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Firma şifresi değiştirildi: {CompanyId}", company.Id);
        return Ok(new { message = "Şifreniz başarıyla güncellendi." });
    }

    private static CompanyProfileDto MapToProfileDto(Models.Company company) => new()
    {
        Id = company.Id,
        Name = company.Name,
        Email = company.Email,
        Phone = company.Phone,
        Address = company.Address,
        CodePrefix = company.CodePrefix,
        WhatsAppPhoneNumberId = company.WhatsAppPhoneNumberId,
        CreatedAt = company.CreatedAt
    };
}

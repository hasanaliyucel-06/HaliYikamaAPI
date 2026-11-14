using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HaliYikamaAPI.Data;
using HaliYikamaAPI.DTOs;
using HaliYikamaAPI.Services.Auth;

namespace HaliYikamaAPI.Controllers;

/// <summary>
/// Firma yöneticileri ve sürücüler için kimlik doğrulama API'si.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IJwtTokenService _jwtService;
    private readonly ICurrentTenantService _tenantService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        ApplicationDbContext context,
        IJwtTokenService jwtService,
        ICurrentTenantService tenantService,
        ILogger<AuthController> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _tenantService = tenantService;
        _logger = logger;
    }

    /// <summary>
    /// Firma yönetici girişi (E-posta ve şifre ile).
    /// </summary>
    [HttpPost("company/login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> CompanyLogin([FromBody] CompanyLoginDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var company = await _context.Companies
            .FirstOrDefaultAsync(c => c.Email.ToLower() == email);

        if (company == null || !PasswordHasher.VerifyPassword(dto.Password, company.PasswordHash))
        {
            _logger.LogWarning("Başarısız firma giriş denemesi: {Email}", dto.Email);
            return Unauthorized(new { message = "E-posta veya şifre hatalı." });
        }

        if (!company.IsActive)
        {
            return Unauthorized(new { message = "Firma hesabı pasife alınmıştır. Lütfen yönetici ile iletişime geçin." });
        }

        var token = _jwtService.GenerateCompanyToken(company);
        _logger.LogInformation("Firma girişi başarılı: {CompanyName} ({CompanyId})", company.Name, company.Id);

        return Ok(new AuthResponseDto
        {
            Token = token,
            Role = "CompanyAdmin",
            Id = company.Id,
            Name = company.Name,
            CompanyId = company.Id,
            CompanyName = company.Name
        });
    }

    /// <summary>
    /// Sürücü hızlı erişim kodu ile giriş (Şifresiz, tek kod ile).
    /// </summary>
    [HttpPost("driver/login")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> DriverLogin([FromBody] DriverLoginDto dto)
    {
        var code = dto.AccessCode.Trim().ToUpperInvariant();
        var driver = await _context.Drivers
            .Include(d => d.Company)
            .FirstOrDefaultAsync(d => d.AccessCode.ToUpper() == code);

        if (driver == null)
        {
            _logger.LogWarning("Geçersiz sürücü kodu denemesi: {Code}", dto.AccessCode);
            return Unauthorized(new { message = "Geçersiz sürücü erişim kodu." });
        }

        if (!driver.IsActive)
        {
            return Unauthorized(new { message = "Bu sürücü hesabı pasife alınmıştır." });
        }

        if (driver.Company == null || !driver.Company.IsActive)
        {
            return Unauthorized(new { message = "Bağlı olduğunuz firma hesabı aktif değildir." });
        }

        var token = _jwtService.GenerateDriverToken(driver, driver.Company.Name);
        _logger.LogInformation("Sürücü girişi başarılı: {DriverName} - {CompanyName}", driver.FullName, driver.Company.Name);

        return Ok(new AuthResponseDto
        {
            Token = token,
            Role = "Driver",
            Id = driver.Id,
            Name = driver.FullName,
            CompanyId = driver.CompanyId,
            CompanyName = driver.Company.Name
        });
    }

    /// <summary>
    /// Mevcut oturum bilgilerini doğrular ve döner.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> GetCurrentUserInfo()
    {
        if (!_tenantService.IsAuthenticated)
        {
            return Unauthorized(new { message = "Oturum açık değil." });
        }

        var company = await _context.Companies.FindAsync(_tenantService.CompanyId);
        if (company == null)
        {
            return Unauthorized(new { message = "Firma bulunamadı." });
        }

        string name = company.Name;
        Guid id = company.Id;

        if (_tenantService.IsDriver && _tenantService.DriverId.HasValue)
        {
            var driver = await _context.Drivers.FindAsync(_tenantService.DriverId.Value);
            if (driver != null)
            {
                name = driver.FullName;
                id = driver.Id;
            }
        }

        return Ok(new AuthResponseDto
        {
            Role = _tenantService.Role ?? "Unknown",
            Id = id,
            Name = name,
            CompanyId = company.Id,
            CompanyName = company.Name
        });
    }
}

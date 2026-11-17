using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using HaliYikamaAPI.Data;
using HaliYikamaAPI.DTOs;
using HaliYikamaAPI.Models;
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
    /// Yeni firma ve yönetici hesabı kaydı oluşturur.
    /// </summary>
    [HttpPost("company/register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthResponseDto>> CompanyRegister([FromBody] CompanyRegisterDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();

        // 1. E-posta kontrolü (DB'deki e-postalar normalize saklanır, index kullanılır)
        var emailExists = await _context.Companies.AnyAsync(c => c.Email == email);
        if (emailExists)
        {
            return BadRequest(new { message = "Bu e-posta adresi ile kayıtlı bir firma zaten mevcut." });
        }

        // 2. Kod öneki tabanını belirle (ASCII normalize — Türkçe karakter sorunu önlenir)
        string basePrefix;
        if (!string.IsNullOrWhiteSpace(dto.CodePrefix))
        {
            basePrefix = NormalizeAsciiPrefix(dto.CodePrefix);
            if (basePrefix.Length < 2)
            {
                return BadRequest(new { message = "Firma ön eki en az 2 İngilizce harf veya rakam içermelidir." });
            }
            if (basePrefix.Length > 5)
            {
                basePrefix = basePrefix[..5];
            }
        }
        else
        {
            var cleanName = NormalizeAsciiPrefix(dto.Name);
            basePrefix = cleanName.Length >= 3 ? cleanName[..3] : cleanName.PadRight(3, 'X');
        }

        // 3. Firma nesnesi oluşturma (eşzamanlı CodePrefix çakışmalarına karşı retry)
        Company? createdCompany = null;
        var passwordHash = PasswordHasher.HashPassword(dto.Password);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            var codePrefix = basePrefix;
            int counter = 1;
            while (await _context.Companies.AnyAsync(c => c.CodePrefix == codePrefix))
            {
                var suffix = counter++.ToString();
                var maxBaseLen = Math.Max(1, 10 - suffix.Length);
                codePrefix = $"{basePrefix[..Math.Min(Math.Min(3, maxBaseLen), basePrefix.Length)]}{suffix}";
            }

            var company = new Company
            {
                Id = Guid.NewGuid(),
                Name = dto.Name.Trim(),
                Email = email,
                PasswordHash = passwordHash,
                Phone = dto.Phone.Trim(),
                Address = string.IsNullOrWhiteSpace(dto.Address) ? null : dto.Address.Trim(),
                CodePrefix = codePrefix,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Companies.Add(company);
            try
            {
                await _context.SaveChangesAsync();
                createdCompany = company;
                break;
            }
            catch (DbUpdateException)
            {
                _context.Entry(company).State = EntityState.Detached;

                // E-posta mı çakıştı yoksa CodePrefix mi ayır
                if (await _context.Companies.AnyAsync(c => c.Email == email))
                {
                    return BadRequest(new { message = "Bu e-posta adresi ile kayıtlı bir firma zaten mevcut." });
                }
            }
        }

        if (createdCompany == null)
        {
            return Conflict(new { message = "Firma kodu üretilirken çakışma oluştu. Lütfen tekrar deneyin." });
        }

        _logger.LogInformation("Yeni firma kaydı tamamlandı: {CompanyName} ({CompanyId}), Önek: {Prefix}",
            createdCompany.Name, createdCompany.Id, createdCompany.CodePrefix);

        var token = _jwtService.GenerateCompanyToken(createdCompany);

        return CreatedAtAction(nameof(GetCurrentUserInfo), new AuthResponseDto
        {
            Token = token,
            Role = "CompanyAdmin",
            Id = createdCompany.Id,
            Name = createdCompany.Name,
            CompanyId = createdCompany.Id,
            CompanyName = createdCompany.Name
        });
    }

    /// <summary>
    /// Firma yönetici girişi (E-posta ve şifre ile).
    /// </summary>
    [HttpPost("company/login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> CompanyLogin([FromBody] CompanyLoginDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var company = await _context.Companies
            .FirstOrDefaultAsync(c => c.Email == email);

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
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> DriverLogin([FromBody] DriverLoginDto dto)
    {
        var code = dto.AccessCode.Trim().ToUpperInvariant();
        var driver = await _context.Drivers
            .Include(d => d.Company)
            .FirstOrDefaultAsync(d => d.AccessCode == code);

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
    [Authorize]
    [ProducesResponseType(typeof(AuthResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> GetCurrentUserInfo()
    {
        if (!_tenantService.IsAuthenticated)
        {
            return Unauthorized(new { message = "Oturum açık değil." });
        }

        var company = await _context.Companies.FindAsync(_tenantService.CompanyId);
        if (company == null || !company.IsActive)
        {
            return Unauthorized(new { message = "Firma bulunamadı veya pasif durumda." });
        }

        string name = company.Name;
        Guid id = company.Id;

        if (_tenantService.IsDriver && _tenantService.DriverId.HasValue)
        {
            var driver = await _context.Drivers.FindAsync(_tenantService.DriverId.Value);
            if (driver == null || !driver.IsActive || driver.CompanyId != company.Id)
            {
                return Unauthorized(new { message = "Sürücü hesabı bulunamadı veya pasif durumda." });
            }

            name = driver.FullName;
            id = driver.Id;
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

    private static string NormalizeAsciiPrefix(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value.Trim())
        {
            var mapped = ch switch
            {
                'ç' or 'Ç' => 'C',
                'ğ' or 'Ğ' => 'G',
                'ı' or 'I' or 'i' or 'İ' => 'I',
                'ö' or 'Ö' => 'O',
                'ş' or 'Ş' => 'S',
                'ü' or 'Ü' => 'U',
                _ => char.ToUpperInvariant(ch)
            };

            if ((mapped >= 'A' && mapped <= 'Z') || (mapped >= '0' && mapped <= '9'))
            {
                sb.Append(mapped);
            }
        }
        return sb.ToString();
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using HaliYikamaAPI.Models;

namespace HaliYikamaAPI.Services.Auth;

/// <summary>
/// Doğrulanmış JWT yapılandırma ayarları (imzalama ve doğrulama için tek kaynak).
/// </summary>
public sealed class JwtSettings
{
    public string SecretKey { get; init; } = string.Empty;
    public string Issuer { get; init; } = "HaliYikamaAPI";
    public string Audience { get; init; } = "HaliYikamaClients";
}

/// <summary>
/// Firma ve sürücüler için JWT token üretici servis.
/// </summary>
public interface IJwtTokenService
{
    string GenerateCompanyToken(Company company);
    string GenerateDriverToken(Driver driver, string companyName);
}

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtSettings _jwtSettings;

    public JwtTokenService(JwtSettings jwtSettings)
    {
        _jwtSettings = jwtSettings;
    }

    public string GenerateCompanyToken(Company company)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, company.Id.ToString()),
            new Claim(ClaimTypes.Email, company.Email),
            new Claim(ClaimTypes.Name, company.Name),
            new Claim(ClaimTypes.Role, "CompanyAdmin"),
            new Claim("companyId", company.Id.ToString()),
            new Claim("companyName", company.Name)
        };

        // Firma yöneticisi için 14 gün geçerli token
        return GenerateToken(claims, DateTime.UtcNow.AddDays(14));
    }

    public string GenerateDriverToken(Driver driver, string companyName)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, driver.Id.ToString()),
            new Claim(ClaimTypes.Name, driver.FullName),
            new Claim(ClaimTypes.Role, "Driver"),
            new Claim("companyId", driver.CompanyId.ToString()),
            new Claim("companyName", companyName),
            new Claim("driverId", driver.Id.ToString()),
            new Claim("accessCode", driver.AccessCode)
        };

        // Sürücüler için 180 gün kalıcı oturum (uygulamayı her açtığında şifre sormaz)
        return GenerateToken(claims, DateTime.UtcNow.AddDays(180));
    }

    private string GenerateToken(IEnumerable<Claim> claims, DateTime expires)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.SecretKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

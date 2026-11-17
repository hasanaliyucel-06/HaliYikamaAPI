using System.Security.Claims;

namespace HaliYikamaAPI.Services.Auth;

/// <summary>
/// Aktif HTTP isteğindeki oturum açmış firmanın ve sürücünün kimlik bilgilerini sağlar.
/// </summary>
public interface ICurrentTenantService
{
    Guid CompanyId { get; }
    Guid? DriverId { get; }
    string? Role { get; }
    bool IsAuthenticated { get; }
    bool IsCompanyAdmin { get; }
    bool IsDriver { get; }
}

public class CurrentTenantService : ICurrentTenantService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentTenantService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public string? Role => User?.FindFirst(ClaimTypes.Role)?.Value;

    public bool IsCompanyAdmin => Role == "CompanyAdmin";

    public bool IsDriver => Role == "Driver";

    public Guid CompanyId
    {
        get
        {
            var claim = User?.FindFirst("companyId");
            if (claim != null && Guid.TryParse(claim.Value, out var companyId))
            {
                return companyId;
            }

            // Giriş yapılmamışsa veya yetkisiz istekte veri sızmasını önlemek için Empty dön
            return Guid.Empty;
        }
    }

    public Guid? DriverId
    {
        get
        {
            var claim = User?.FindFirst("driverId");
            if (claim != null && Guid.TryParse(claim.Value, out var driverId))
            {
                return driverId;
            }
            return null;
        }
    }
}

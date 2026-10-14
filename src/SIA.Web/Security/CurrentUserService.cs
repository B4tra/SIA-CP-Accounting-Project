using System.Security.Claims;
using SIA.Application.Common.Interfaces;

namespace SIA.Web.Security;

/// <summary>
/// Implementasi <see cref="ICurrentUserService"/> untuk Blazor Server: memakai user circuit
/// (mode interaktif) atau <c>HttpContext.User</c> (request HTTP biasa / prerender).
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CircuitUserAccessor _circuitUserAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor, CircuitUserAccessor circuitUserAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
        _circuitUserAccessor = circuitUserAccessor;
    }

    private ClaimsPrincipal? Principal => _circuitUserAccessor.User ?? _httpContextAccessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string? UserName => Principal?.Identity?.Name;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public string? IpAddress => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
}

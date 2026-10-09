using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Moq;
using SIA.Application.Common.Constants;
using SIA.Web.Services;

namespace SIA.Tests.Web;

public class CurrentUserServiceTests
{
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly CurrentUserService _currentUserService;

    public CurrentUserServiceTests()
    {
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        _currentUserService = new CurrentUserService(_httpContextAccessorMock.Object);
    }

    [Fact]
    public void UserId_WhenNoHttpContext_ReturnsNull()
    {
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        var result = _currentUserService.UserId;

        Assert.Null(result);
    }

    [Fact]
    public void IsAuthenticated_WhenNoHttpContext_ReturnsFalse()
    {
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        var result = _currentUserService.IsAuthenticated;

        Assert.False(result);
    }

    [Fact]
    public void Properties_WhenUserAuthenticated_ReturnCorrectValues()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user-42"),
            new(ClaimTypes.Name, "admin@sia.local"),
            new(ClaimTypes.Role, Roles.Admin)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var claimsPrincipal = new ClaimsPrincipal(identity);

        var httpContext = new DefaultHttpContext
        {
            User = claimsPrincipal
        };
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("127.0.0.1");

        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        Assert.Equal("user-42", _currentUserService.UserId);
        Assert.True(_currentUserService.IsAuthenticated);
        Assert.True(_currentUserService.IsInRole(Roles.Admin));
        Assert.False(_currentUserService.IsInRole(Roles.Staff));
        Assert.Equal("127.0.0.1", _currentUserService.IpAddress);
    }

    [Fact]
    public void IsInRole_WhenAnonymous_ReturnsFalse()
    {
        var httpContext = new DefaultHttpContext();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);

        Assert.False(_currentUserService.IsInRole(Roles.Admin));
        Assert.False(_currentUserService.IsAuthenticated);
    }
}

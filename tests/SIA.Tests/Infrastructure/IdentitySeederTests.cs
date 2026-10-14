using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SIA.Application.Common.Constants;
using SIA.Infrastructure.Identity;

namespace SIA.Tests.Infrastructure;

public class IdentitySeederTests
{
    private const string Password = "Rahasia123";

    private static Mock<RoleManager<IdentityRole>> CreateRoleManager() =>
        new(Mock.Of<IRoleStore<IdentityRole>>(), null!, null!, null!, null!);

    private static Mock<UserManager<ApplicationUser>> CreateUserManager() =>
        new(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

    [Fact]
    public async Task SeedRoles_MembuatSemuaRole_JikaBelumAda()
    {
        var roleManager = CreateRoleManager();
        roleManager.Setup(r => r.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        roleManager.Setup(r => r.CreateAsync(It.IsAny<IdentityRole>())).ReturnsAsync(IdentityResult.Success);

        await IdentitySeeder.SeedRolesAsync(roleManager.Object, NullLogger.Instance);

        foreach (var role in Roles.All)
        {
            roleManager.Verify(r => r.CreateAsync(It.Is<IdentityRole>(x => x.Name == role)), Times.Once);
        }
    }

    [Fact]
    public async Task SeedRoles_TidakMembuatUlang_JikaSudahAda()
    {
        var roleManager = CreateRoleManager();
        roleManager.Setup(r => r.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(true);

        await IdentitySeeder.SeedRolesAsync(roleManager.Object, NullLogger.Instance);

        roleManager.Verify(r => r.CreateAsync(It.IsAny<IdentityRole>()), Times.Never);
    }

    [Fact]
    public async Task SeedRoles_Gagal_MelemparException()
    {
        var roleManager = CreateRoleManager();
        roleManager.Setup(r => r.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        roleManager.Setup(r => r.CreateAsync(It.IsAny<IdentityRole>()))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "gagal" }));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => IdentitySeeder.SeedRolesAsync(roleManager.Object, NullLogger.Instance));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SeedTestUsers_DilewatiJikaPasswordKosong(string? password)
    {
        var userManager = CreateUserManager();

        await IdentitySeeder.SeedTestUsersAsync(userManager.Object, password, NullLogger.Instance);

        userManager.Verify(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        userManager.Verify(u => u.FindByEmailAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SeedTestUsers_MembuatSatuAkunPerRole_DenganRoleBenar()
    {
        var userManager = CreateUserManager();
        userManager.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        userManager.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), Password)).ReturnsAsync(IdentityResult.Success);
        userManager.Setup(u => u.IsInRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(false);
        userManager.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);

        await IdentitySeeder.SeedTestUsersAsync(userManager.Object, Password, NullLogger.Instance);

        Assert.Equal(3, IdentitySeeder.TestUsers.Count);
        Assert.Equal(Roles.All.OrderBy(r => r), IdentitySeeder.TestUsers.Select(t => t.Role).OrderBy(r => r));

        foreach (var testUser in IdentitySeeder.TestUsers)
        {
            userManager.Verify(u => u.CreateAsync(
                It.Is<ApplicationUser>(x => x.Email == testUser.Email && x.UserName == testUser.Email
                                            && x.IsActive && x.EmailConfirmed && x.FullName == testUser.FullName),
                Password), Times.Once);
            userManager.Verify(u => u.AddToRoleAsync(
                It.Is<ApplicationUser>(x => x.Email == testUser.Email), testUser.Role), Times.Once);
        }
    }

    [Fact]
    public async Task SeedTestUsers_AkunSudahAda_TidakDibuatUlang()
    {
        var userManager = CreateUserManager();
        userManager.Setup(u => u.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((string email) => new ApplicationUser { Email = email, UserName = email });
        userManager.Setup(u => u.IsInRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(true);

        await IdentitySeeder.SeedTestUsersAsync(userManager.Object, Password, NullLogger.Instance);

        userManager.Verify(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
        userManager.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ClaimsFactory_MenambahkanClaimNamaLengkap()
    {
        var user = new ApplicationUser { Id = "u1", UserName = "admin@sia.local", FullName = "Administrator" };
        var userManager = CreateUserManager();
        userManager.Setup(u => u.GetUserIdAsync(user)).ReturnsAsync(user.Id);
        userManager.Setup(u => u.GetUserNameAsync(user)).ReturnsAsync(user.UserName);
        var roleManager = CreateRoleManager();

        var factory = new AppUserClaimsPrincipalFactory(
            userManager.Object, roleManager.Object, Options.Create(new IdentityOptions()));

        var principal = await factory.CreateAsync(user);

        Assert.Equal("Administrator", principal.FindFirstValue(AppUserClaimsPrincipalFactory.FullNameClaimType));
    }
}

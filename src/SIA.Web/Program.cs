using System.Globalization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MudBlazor.Services;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Infrastructure;
using SIA.Infrastructure.Identity;
using SIA.Web.Components;
using SIA.Web.Security;

// Culture tunggal: Indonesia (Rp 1.250.000, tanggal dd/MM/yyyy).
var culture = new CultureInfo("id-ID");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var builder = WebApplication.CreateBuilder(args);

// Razor Components (Blazor Server) + MudBlazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

// Infrastructure: DbContext, repository, Identity store
builder.Services.AddInfrastructure(builder.Configuration);

// Autentikasi cookie Identity + SignInManager
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/logout";
    options.AccessDeniedPath = "/access-denied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});
new IdentityBuilder(typeof(ApplicationUser), typeof(IdentityRole), builder.Services)
    .AddSignInManager();

// Otorisasi berbasis policy (pemetaan role di SIA.Application.Common.Constants.Policies)
builder.Services.AddAuthorization(options =>
{
    foreach (var (policy, roles) in Policies.RoleMap)
    {
        options.AddPolicy(policy, p => p.RequireAuthenticatedUser().RequireRole(roles));
    }
});
builder.Services.AddCascadingAuthenticationState();

// User saat ini (dipakai service & AuditSaveChangesInterceptor)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CircuitUserAccessor>();
builder.Services.TryAddEnumerable(ServiceDescriptor.Scoped<CircuitHandler, UserCircuitHandler>());
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

var app = builder.Build();

// Seeding Identity: role selalu dipastikan ada; akun uji hanya di Development.
await SeedIdentityAsync(app);

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(culture),
    SupportedCultures = new[] { culture },
    SupportedUICultures = new[] { culture },
});

app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Logout: POST + antiforgery (parameter form mengaktifkan validasi token).
app.MapPost("/logout", async (SignInManager<ApplicationUser> signInManager, [FromForm] string? returnUrl) =>
    {
        await signInManager.SignOutAsync();
        return TypedResults.LocalRedirect("/login");
    })
    .RequireAuthorization();

app.Run();

static async Task SeedIdentityAsync(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeeder");

    try
    {
        await IdentitySeeder.SeedRolesAsync(services.GetRequiredService<RoleManager<IdentityRole>>(), logger);

        if (app.Environment.IsDevelopment())
        {
            await IdentitySeeder.SeedTestUsersAsync(
                services.GetRequiredService<UserManager<ApplicationUser>>(),
                app.Configuration[IdentitySeeder.DefaultPasswordConfigKey],
                logger);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Seeding Identity gagal. Pastikan database sudah dimigrasi dan dapat diakses.");
    }
}

using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using MudBlazor.Services;
using SIA.Application.Common.Constants;
using SIA.Application.Common.Interfaces;
using SIA.Infrastructure;
using SIA.Infrastructure.Identity;
using SIA.Web.Components;
using SIA.Web.Data;
using SIA.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Konfigurasi Culture ke id-ID (seluruh teks antarmuka berbahasa Indonesia, uang Rp)
var indonesianCulture = new CultureInfo("id-ID");
CultureInfo.DefaultThreadCurrentCulture = indonesianCulture;
CultureInfo.DefaultThreadCurrentUICulture = indonesianCulture;

// 2. Registrasi Services
builder.Services.AddInfrastructure(builder.Configuration);

// Identity authentication & cookies untuk Blazor Server
builder.Services.AddIdentityCore<ApplicationUser>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/api/auth/logout";
    options.AccessDeniedPath = "/access-denied";
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

// Authorization Policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(Roles.Admin));
    options.AddPolicy("StaffOrAdmin", policy => policy.RequireRole(Roles.Admin, Roles.Staff));
    options.AddPolicy("ViewerOrAdmin", policy => policy.RequireRole(Roles.Admin, Roles.Viewer));
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// MudBlazor Services
builder.Services.AddMudServices();

// Razor Components
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// 3. Eksekusi Seeder (3 Role & Akun Uji)
await DbSeeder.SeedAsync(app.Services);

// 4. HTTP Pipeline
var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(indonesianCulture),
    SupportedCultures = [indonesianCulture],
    SupportedUICultures = [indonesianCulture]
};
app.UseRequestLocalization(localizationOptions);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Minimal API Endpoints untuk Login & Logout
app.MapPost("/api/auth/login", async (HttpContext context, SignInManager<ApplicationUser> signInManager) =>
{
    var form = await context.Request.ReadFormAsync();
    var email = form["email"].ToString();
    var password = form["password"].ToString();
    var returnUrl = form["returnUrl"].ToString();

    var result = await signInManager.PasswordSignInAsync(email, password, isPersistent: true, lockoutOnFailure: false);
    if (result.Succeeded)
    {
        var target = string.IsNullOrWhiteSpace(returnUrl) || !returnUrl.StartsWith("/") || returnUrl.StartsWith("//")
            ? "/"
            : returnUrl;
        return Results.LocalRedirect(target);
    }

    var errorTarget = string.IsNullOrWhiteSpace(returnUrl)
        ? "/login?error=1"
        : $"/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl)}";

    return Results.LocalRedirect(errorTarget);
});

app.MapPost("/api/auth/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.LocalRedirect("/login");
});

app.MapGet("/logout", async (SignInManager<ApplicationUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.LocalRedirect("/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

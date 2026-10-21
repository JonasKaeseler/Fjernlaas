using Fjernlaas.Web.Components;
using Fjernlaas.Web.Data;
using Fjernlaas.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHttpContextAccessor();

builder.Services
    .AddAuthentication(o =>
    {
        o.DefaultScheme = IdentityConstants.ApplicationScheme;
        o.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

builder.Services
    .AddIdentityCore<ApplicationUser>(o => o.User.RequireUniqueEmail = true)
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
    .AddSignInManager<AppSignInManager>();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = ".Fjernlaas.Auth";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    // Drift kører bag HTTPS (FND-7). Lokalt i Development tillades http.
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    o.LoginPath = "/login";
    o.AccessDeniedPath = "/login";
    o.ExpireTimeSpan = TimeSpan.FromDays(30);
    o.SlidingExpiration = true;
});

// Sessionen valideres mod databasen hvert minut, så en deaktiveret bruger hurtigt mister adgang.
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.FromMinutes(1));

// Alt kræver login, medmindre siden eller endpointet udtrykkeligt er åbent.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
    .AddPolicy(Politikker.PlatformAdmin, p => p.RequireRole(Roller.PlatformAdmin));

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddScoped<OrganisationService>();
builder.Services.AddScoped<PlatformAdminSeeder>();
builder.Services.AddScoped<LoginService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

// Logout er en POST med antiforgery-token, så et fremmed link ikke kan logge brugeren ud.
app.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery, LoginService login) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.BadRequest();
    }

    await login.LogUdAsync();
    return Results.Redirect("/login");
});

// Platformsadmin oprettes ved opstart, hvis e-mail og adgangskode er sat i konfigurationen
// (user-secrets lokalt, miljøvariabler i drift). Uden dem sker der ingenting.
var adminEmail = app.Configuration["PlatformAdmin:Email"];
var adminAdgangskode = app.Configuration["PlatformAdmin:Password"];
if (!string.IsNullOrWhiteSpace(adminEmail) && !string.IsNullOrWhiteSpace(adminAdgangskode))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>()
        .SikrAsync(adminEmail, app.Configuration["PlatformAdmin:Navn"] ?? "Platformsadmin", adminAdgangskode);
}

app.Run();

// Gør Program tilgængelig for integrationstests (WebApplicationFactory).
public partial class Program { }

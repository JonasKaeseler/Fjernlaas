using Fjernlaas.Web.Components;
using Fjernlaas.Web.Data;
using Fjernlaas.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddIdentityCore<ApplicationUser>(o => o.User.RequireUniqueEmail = true)
    .AddRoles<IdentityRole<Guid>>()
    .AddEntityFrameworkStores<AppDbContext>();

builder.Services.AddScoped<OrganisationService>();
builder.Services.AddScoped<PlatformAdminSeeder>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

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

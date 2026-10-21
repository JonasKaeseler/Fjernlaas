using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Fjernlaas.Web.Data;

/// <summary>
/// Bruges kun af dotnet-ef (migrations). Kræver ingen kørende database for at
/// oprette en migration. Overstyr med miljøvariablen FJERNLAAS_DB hvis nødvendigt.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("FJERNLAAS_DB")
            ?? "Server=localhost,14330;Database=Fjernlaas;User Id=sa;Password=Fjernlaas_Dev_Pass1!;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}

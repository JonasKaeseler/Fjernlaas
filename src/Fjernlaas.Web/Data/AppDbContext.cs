using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Fjernlaas.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Organisation> Organisationer => Set<Organisation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Organisation>(e =>
        {
            e.ToTable("Organisation");
            e.HasKey(x => x.Id);
            e.Property(x => x.Navn).HasMaxLength(200).IsRequired();
            e.Property(x => x.NavnNormaliseret).HasMaxLength(200).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.NavnNormaliseret).IsUnique();
        });

        builder.Entity<ApplicationUser>(e =>
        {
            e.ToTable("Bruger");
            e.Property(x => x.Navn).HasMaxLength(200).IsRequired();

            // Restrict: en organisation med brugere kan ikke slettes ved et uheld.
            e.HasOne(x => x.Organisation)
                .WithMany(o => o.Brugere)
                .HasForeignKey(x => x.OrganisationId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.OrganisationId);

            // E-mail skal være unik uanset store og små bogstaver. Identity gemmer
            // NormalizedEmail, så vi lægger det unikke indeks på den kolonne.
            e.HasIndex(x => x.NormalizedEmail)
                .IsUnique()
                .HasFilter("[NormalizedEmail] IS NOT NULL");
        });
    }
}

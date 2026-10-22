using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Fjernlaas.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Organisation> Organisationer => Set<Organisation>();
    public DbSet<Lokation> Lokationer => Set<Lokation>();
    public DbSet<Ressource> Ressourcer => Set<Ressource>();
    public DbSet<TTLockForbindelse> TTLockForbindelser => Set<TTLockForbindelse>();
    public DbSet<SmsNummer> SmsNumre => Set<SmsNummer>();
    public DbSet<Noegleord> Noegleordene => Set<Noegleord>();
    public DbSet<Oplaasningsforsoeg> Forsoeg => Set<Oplaasningsforsoeg>();

    // Tenant-afgrænsning for lokationer og ressourcer. Standard er ingen adgang: en kontekst, hvor
    // ingen har sagt, hvem der spørger, ser ingen lokationer eller ressourcer. Services kalder
    // SaetTenant med aktøren. Kun system-opgaver (fx SMS-webhooken) bruger IgnoreQueryFilters().
    private bool _tenantAlle;
    private Guid? _tenantOrganisationId;

    public void SaetTenant(bool alleOrganisationer, Guid? organisationId)
    {
        _tenantAlle = alleOrganisationer;
        _tenantOrganisationId = organisationId;
    }

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

        builder.Entity<Lokation>(e =>
        {
            e.ToTable("Lokation");
            e.HasKey(x => x.Id);
            e.Property(x => x.Navn).HasMaxLength(200).IsRequired();
            e.Property(x => x.NavnNormaliseret).HasMaxLength(200).IsRequired();
            e.Property(x => x.Adresse).HasMaxLength(300);
            e.Property(x => x.Tidszone).HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.HasOne<Organisation>().WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.OrganisationId, x.NavnNormaliseret }).IsUnique();

            e.HasQueryFilter(x => _tenantAlle || x.OrganisationId == _tenantOrganisationId);
        });

        builder.Entity<Ressource>(e =>
        {
            e.ToTable("Ressource");
            e.HasKey(x => x.Id);
            e.Property(x => x.Navn).HasMaxLength(200).IsRequired();
            e.Property(x => x.NavnNormaliseret).HasMaxLength(200).IsRequired();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            // Restrict overalt: to stier fra Organisation (direkte og via Lokation) ville ellers
            // give flere kaskade-stier, som SQL Server afviser. Vi sletter alligevel ikke, vi deaktiverer.
            e.HasOne(x => x.Lokation).WithMany(l => l.Ressourcer).HasForeignKey(x => x.LokationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Organisation>().WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne<TTLockForbindelse>().WithMany().HasForeignKey(x => x.ForbindelseId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.LokationId, x.NavnNormaliseret }).IsUnique();
            e.HasIndex(x => x.OrganisationId);

            e.HasQueryFilter(x => _tenantAlle || x.OrganisationId == _tenantOrganisationId);
        });

        builder.Entity<TTLockForbindelse>(e =>
        {
            e.ToTable("TTLockForbindelse");
            e.HasKey(x => x.Id);
            e.Property(x => x.Navn).HasMaxLength(200).IsRequired();
            e.Property(x => x.TTLockBrugernavn).HasMaxLength(256).IsRequired();
            e.Property(x => x.AdgangskodeHashBeskyttet).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            e.Property(x => x.SidsteFejl).HasMaxLength(500);

            e.HasOne<Organisation>().WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.OrganisationId);

            e.HasQueryFilter(x => _tenantAlle || x.OrganisationId == _tenantOrganisationId);
        });

        builder.Entity<SmsNummer>(e =>
        {
            e.ToTable("SmsNummer");
            e.HasKey(x => x.Id);
            e.Property(x => x.Navn).HasMaxLength(200).IsRequired();
            e.Property(x => x.Nummer).HasMaxLength(32);
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.TokenPraefiks).HasMaxLength(16).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.HasOne<Organisation>().WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.OrganisationId);

            e.HasQueryFilter(x => _tenantAlle || x.OrganisationId == _tenantOrganisationId);
        });

        builder.Entity<Noegleord>(e =>
        {
            e.ToTable("Noegleord");
            e.HasKey(x => x.Id);
            e.Property(x => x.Ord).HasMaxLength(100).IsRequired();
            e.Property(x => x.OrdNormaliseret).HasMaxLength(100).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

            e.HasOne<Organisation>().WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SmsNummer>().WithMany().HasForeignKey(x => x.SmsNummerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Ressource>().WithMany().HasForeignKey(x => x.RessourceId).OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.SmsNummerId, x.OrdNormaliseret }).IsUnique();
            e.HasIndex(x => x.RessourceId);

            e.HasQueryFilter(x => _tenantAlle || x.OrganisationId == _tenantOrganisationId);
        });

        builder.Entity<Oplaasningsforsoeg>(e =>
        {
            e.ToTable("Oplaasningsforsoeg");
            e.HasKey(x => x.Id);
            e.Property(x => x.Afsender).HasMaxLength(32).IsRequired();
            e.Property(x => x.Tekst).HasMaxLength(500).IsRequired();
            e.Property(x => x.Noegleord).HasMaxLength(100);
            e.Property(x => x.Udfald).HasConversion<string>().HasMaxLength(40).IsRequired();
            e.Property(x => x.Fejltekst).HasMaxLength(500);

            e.HasOne<Organisation>().WithMany().HasForeignKey(x => x.OrganisationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SmsNummer>().WithMany().HasForeignKey(x => x.SmsNummerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Ressource>().WithMany().HasForeignKey(x => x.RessourceId).OnDelete(DeleteBehavior.Restrict);

            // Grænsen pr. afsender og dublet-tjekket slår op på nummer, afsender og tid.
            e.HasIndex(x => new { x.SmsNummerId, x.Afsender, x.TidspunktUtc });
            e.HasIndex(x => new { x.OrganisationId, x.TidspunktUtc });

            e.HasQueryFilter(x => _tenantAlle || x.OrganisationId == _tenantOrganisationId);
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

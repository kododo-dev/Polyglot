using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kododo.Polyglot.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public const string Schema = "app";

    public DbSet<User> Users => Set<User>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<TranslationChange> TranslationChanges => Set<TranslationChange>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(u => u.Id);
            e.Property(u => u.Username).HasMaxLength(64).IsRequired();
            e.Property(u => u.NormalizedUsername).HasMaxLength(64).IsRequired();
            e.Property(u => u.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(16);
            e.Property(u => u.ExternalIssuer).HasMaxLength(512);
            e.Property(u => u.ExternalSubject).HasMaxLength(256);
            e.HasIndex(u => u.NormalizedUsername).IsUnique();
            e.HasIndex(u => new { u.ExternalIssuer, u.ExternalSubject }).IsUnique();
        });

        modelBuilder.Entity<ApiKey>(e =>
        {
            e.ToTable("api_keys");
            e.HasKey(k => k.Id);
            e.Property(k => k.KeyId).HasMaxLength(8).IsRequired();
            e.Property(k => k.SecretHash).HasMaxLength(32).IsRequired();
            e.Property(k => k.Name).HasMaxLength(200).IsRequired();
            e.Property(k => k.Scopes).HasMaxLength(200).IsRequired();
            e.HasIndex(k => k.KeyId).IsUnique();
        });

        modelBuilder.Entity<TranslationChange>(e =>
        {
            e.ToTable("translation_changes");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).UseIdentityAlwaysColumn();
            e.Property(c => c.Key).IsRequired();
            e.Property(c => c.Culture).IsRequired();
            e.Property(c => c.UserName).HasMaxLength(200).IsRequired();
        });

        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
    }
}

using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Kododo.Polyglot.Web.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IDataProtectionKeyContext
{
    public const string Schema = "app";

    public DbSet<User> Users => Set<User>();

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

        modelBuilder.Entity<DataProtectionKey>().ToTable("data_protection_keys");
    }
}

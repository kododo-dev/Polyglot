using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kododo.Polyglot.Web.Data;

/// <summary>Used only by <c>dotnet ef</c> to create migrations without starting the app.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time", o => o.MigrationsHistoryTable("__ef_migrations", AppDbContext.Schema))
            .Options;
        return new AppDbContext(options);
    }
}

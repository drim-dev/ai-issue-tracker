using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Api.Common.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef</c> for migrations. At runtime the
/// context is configured by the Aspire PostgreSQL integration instead; this
/// connection string is a placeholder and is never used to reach a database.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=appdb;Username=postgres;Password=postgres")
            .Options;

        return new AppDbContext(options);
    }
}

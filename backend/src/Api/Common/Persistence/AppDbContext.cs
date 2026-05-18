using Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Api.Common.Persistence;

/// <summary>
/// EF Core context for the AI Issue Tracker. Entity sets are added as
/// domain features are implemented.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}

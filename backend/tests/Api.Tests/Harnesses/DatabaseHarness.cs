using System.Collections;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace Api.Tests.Harnesses;

/// <summary>
/// Runs a real PostgreSQL instance in a container, points the SUT at it,
/// and provides seed / query / reset helpers for component tests.
/// </summary>
public class DatabaseHarness<TProgram, TDbContext> : IHarness<TProgram>
    where TProgram : class
    where TDbContext : DbContext
{
    private readonly string _connectionStringName;
    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<TProgram>? _factory;
    private Respawner? _respawner;
    private bool _started;

    public DatabaseHarness(string connectionStringName)
    {
        _connectionStringName = connectionStringName;
    }

    public void ConfigureWebHostBuilder(IWebHostBuilder builder)
    {
        builder.UseSetting(
            $"ConnectionStrings:{_connectionStringName}",
            _postgres!.GetConnectionString());
    }

    public async Task Start(WebApplicationFactory<TProgram> factory, CancellationToken cancellationToken)
    {
        _factory = factory;

        _postgres = new PostgreSqlBuilder("postgres:17-alpine")
            .Build();

        await _postgres.StartAsync(cancellationToken);
        _started = true;
    }

    public async Task Stop(CancellationToken cancellationToken)
    {
        if (_postgres is not null)
        {
            await _postgres.StopAsync(cancellationToken);
            await _postgres.DisposeAsync();
        }

        _started = false;
    }

    /// <summary>Applies EF Core migrations to build the database schema.</summary>
    public async Task Migrate(CancellationToken cancellationToken)
    {
        ThrowIfNotStarted();

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }

    public async Task Save(params object[] entities)
    {
        ThrowIfNotStarted();

        await using var scope = _factory!.Services.CreateAsyncScope();
        await using var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        foreach (var collection in entities.OfType<IEnumerable>())
        {
            db.AddRange(collection);
        }

        db.AddRange(entities.Where(e => e is not IEnumerable));

        await db.SaveChangesAsync();
    }

    public async Task Execute(Func<TDbContext, Task> action)
    {
        ThrowIfNotStarted();

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await action(db);
    }

    public async Task<TResult> Execute<TResult>(Func<TDbContext, Task<TResult>> action)
    {
        ThrowIfNotStarted();

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        return await action(db);
    }

    public async Task<TEntity?> SingleOrDefault<TEntity>(
        Expression<Func<TEntity, bool>> predicate,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        ThrowIfNotStarted();

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        return await db.Set<TEntity>().SingleOrDefaultAsync(predicate, cancellationToken);
    }

    public async Task Clear(CancellationToken cancellationToken)
    {
        ThrowIfNotStarted();

        await using var connection = new NpgsqlConnection(_postgres!.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        // Built lazily: Respawn requires at least one table, which only exists
        // once the schema has been migrated. An empty schema is a no-op.
        if (_respawner is null)
        {
            try
            {
                _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
                {
                    SchemasToInclude = ["public"],
                    TablesToIgnore = ["__EFMigrationsHistory"],
                    DbAdapter = DbAdapter.Postgres,
                });
            }
            catch (InvalidOperationException)
            {
                return;
            }
        }

        await _respawner.ResetAsync(connection);
    }

    private void ThrowIfNotStarted()
    {
        if (!_started)
        {
            throw new InvalidOperationException(
                $"Database harness is not started. Call {nameof(Start)} first.");
        }
    }
}

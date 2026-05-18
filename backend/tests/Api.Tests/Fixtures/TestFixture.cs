using Api.Common.Persistence;
using Api.Tests.Harnesses;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using static Api.Tests.Fixtures.TestCancellation;

namespace Api.Tests.Fixtures;

/// <summary>
/// Shared component-test fixture: spins up the API against a real PostgreSQL
/// container once per xUnit collection and resets data between tests.
/// </summary>
public class TestFixture : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;

    public TestFixture()
    {
        Database = new DatabaseHarness<Program, AppDbContext>("appdb");
        HttpClient = new HttpClientHarness<Program>();

        _factory = new WebApplicationFactory<Program>()
            .AddHarness(Database)
            .AddHarness(HttpClient);
    }

    public WebApplicationFactory<Program> Factory => _factory;
    public DatabaseHarness<Program, AppDbContext> Database { get; }
    public HttpClientHarness<Program> HttpClient { get; }

    public Task Reset(CancellationToken cancellationToken) =>
        Database.Clear(cancellationToken);

    /// <summary>Resolves a scoped service from the running application.</summary>
    public async Task<TResult> WithService<TService, TResult>(Func<TService, Task<TResult>> action)
        where TService : notnull
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        return await action(service);
    }

    /// <summary>
    /// Creates an HTTP client carrying the BFF's trusted <c>X-User-Id</c> header,
    /// emulating an authenticated request.
    /// </summary>
    public HttpClient CreateAuthedClient(long userId)
    {
        var client = HttpClient.CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", userId.ToString());
        return client;
    }

    public async Task InitializeAsync()
    {
        await Database.Start(_factory, CreateCancellationToken(120));
        await HttpClient.Start(_factory, CreateCancellationToken());

        // Accessing Server boots the host, which applies EF Core migrations.
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        await HttpClient.Stop(CreateCancellationToken());
        await Database.Stop(CreateCancellationToken());
        await _factory.DisposeAsync();
    }

    // Workaround for FluentAssertions concurrency issue with date comparisons.
    // https://github.com/fluentassertions/fluentassertions/issues/1932#issuecomment-1137366562
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void SetupFluentAssertions()
    {
        AssertionOptions.AssertEquivalencyUsing(options => options
            .Using<DateTimeOffset>(ctx => ctx.Subject.Should().BeSameDateAs(ctx.Expectation))
            .WhenTypeIs<DateTimeOffset>()
            .Using<DateTime>(ctx => ctx.Subject.Should().BeSameDateAs(ctx.Expectation))
            .WhenTypeIs<DateTime>());
    }
}

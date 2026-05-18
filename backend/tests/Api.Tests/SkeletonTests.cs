using System.Net;
using Api.Tests.Fixtures;
using FluentAssertions;
using static Api.Tests.Fixtures.TestCancellation;

namespace Api.Tests;

/// <summary>
/// Doubles as the component-test harness smoke test: confirms the API boots
/// against a real PostgreSQL container and serves requests.
/// </summary>
[CollectionDefinition(Name)]
public class SkeletonTestsCollection : ICollectionFixture<TestFixture>
{
    public const string Name = nameof(SkeletonTestsCollection);
}

[Collection(SkeletonTestsCollection.Name)]
public class SkeletonTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public SkeletonTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Api_responds_to_liveness_probe()
    {
        var client = _fixture.HttpClient.CreateClient();

        var response = await client.GetAsync("/alive", CreateCancellationToken());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

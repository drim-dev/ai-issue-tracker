using System.Net;
using System.Net.Http.Json;
using Api.Domain;
using Api.Tests.Fixtures;
using FluentAssertions;
using static Api.Tests.Fixtures.TestCancellation;

namespace Api.Tests.Features.Auth;

[Collection(AuthTestsCollection.Name)]
public class GetCurrentUserTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public GetCurrentUserTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Should_return_current_user_with_x_user_id_header()
    {
        var user = new User
        {
            Id = 7,
            Email = "sam@example.com",
            Name = "Sam Carter",
            PasswordHash = "hash",
            CreatedAt = DateTime.UtcNow,
        };
        await _fixture.Database.Save(user);

        var client = _fixture.CreateAuthedClient(user.Id);
        var response = await client.GetAsync("/auth/me", CreateCancellationToken());

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<UserResponseContract>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(user.Id.ToString());
        body.Email.Should().Be("sam@example.com");
        body.Name.Should().Be("Sam Carter");
    }

    [Fact]
    public async Task Should_return_401_without_x_user_id_header()
    {
        var client = _fixture.HttpClient.CreateClient();

        var response = await client.GetAsync("/auth/me", CreateCancellationToken());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsContract>();
        problem!.ErrorCode.Should().Be("auth:unauthorized");
    }

    [Fact]
    public async Task Should_return_404_when_user_was_deleted()
    {
        // Valid session header, but the account no longer exists.
        var client = _fixture.CreateAuthedClient(999);

        var response = await client.GetAsync("/auth/me", CreateCancellationToken());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsContract>();
        problem!.ErrorCode.Should().Be("auth:user:get:not_found");
    }
}

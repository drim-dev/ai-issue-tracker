using System.Net;
using System.Net.Http.Json;
using Api.Common.Auth;
using Api.Domain;
using Api.Features.Auth;
using Api.Tests.Fixtures;
using FluentAssertions;
using FluentValidation.TestHelper;
using static Api.Tests.Fixtures.TestCancellation;

namespace Api.Tests.Features.Auth;

[Collection(AuthTestsCollection.Name)]
public class LoginTests : IAsyncLifetime
{
    private const string Password = "Qwer1234!";
    private readonly TestFixture _fixture;

    public LoginTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpResponseMessage> Act(LoginRequestContract request)
    {
        var client = _fixture.HttpClient.CreateClient();
        return await client.PostAsJsonAsync("/auth/login", request, CreateCancellationToken());
    }

    [Fact]
    public async Task Should_authenticate_and_return_200()
    {
        var user = await SeedUser("sam@example.com");

        var response = await Act(new LoginRequestContract("Sam@Example.com", Password));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<UserResponseContract>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(user.Id.ToString());
        body.Email.Should().Be("sam@example.com");
    }

    [Fact]
    public async Task Should_return_401_for_wrong_password()
    {
        await SeedUser("sam@example.com");

        var response = await Act(new LoginRequestContract("sam@example.com", "WrongPass1!"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsContract>();
        problem!.ErrorCode.Should().Be("auth:login:invalid_credentials");
    }

    [Fact]
    public async Task Should_return_401_for_unknown_email()
    {
        var response = await Act(new LoginRequestContract("nobody@example.com", Password));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsContract>();
        problem!.ErrorCode.Should().Be("auth:login:invalid_credentials");
    }

    private async Task<User> SeedUser(string email)
    {
        var hash = await _fixture.WithService<IPasswordHasher, string>(
            h => Task.FromResult(h.Hash(Password)));

        var user = new User
        {
            Id = 42,
            Email = email,
            Name = "Sam Carter",
            PasswordHash = hash,
            CreatedAt = DateTime.UtcNow,
        };
        await _fixture.Database.Save(user);
        return user;
    }

    public class ValidatorTests
    {
        private readonly Login.RequestValidator _validator = new();

        [Fact]
        public void Should_reject_empty_email()
        {
            var result = _validator.TestValidate(new Login.Request("", Password));
            result.ShouldHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public void Should_reject_empty_password()
        {
            var result = _validator.TestValidate(new Login.Request("sam@example.com", ""));
            result.ShouldHaveValidationErrorFor(x => x.Password);
        }

        [Fact]
        public void Should_accept_valid_credentials()
        {
            var result = _validator.TestValidate(new Login.Request("sam@example.com", Password));
            result.ShouldNotHaveAnyValidationErrors();
        }
    }
}

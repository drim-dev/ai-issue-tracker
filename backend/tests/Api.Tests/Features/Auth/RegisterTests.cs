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
public class RegisterTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public RegisterTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<HttpResponseMessage> Act(RegisterRequestContract request)
    {
        var client = _fixture.HttpClient.CreateClient();
        return await client.PostAsJsonAsync("/auth/register", request, CreateCancellationToken());
    }

    [Fact]
    public async Task Should_register_user_and_return_201()
    {
        var request = new RegisterRequestContract("Sam@Example.com", "Sam Carter", "Qwer1234!");

        var response = await Act(request);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<UserResponseContract>();
        body.Should().NotBeNull();
        body!.Email.Should().Be("sam@example.com");
        body.Name.Should().Be("Sam Carter");
        body.Avatar.Should().BeNull();
        body.Id.Should().NotBeNullOrEmpty();

        // Side effect: user persisted with a normalized email and a real hash.
        var dbUser = await _fixture.Database.SingleOrDefault<User>(
            u => u.Email == "sam@example.com", CreateCancellationToken());
        dbUser.Should().NotBeNull();
        dbUser!.Id.ToString().Should().Be(body.Id);
        dbUser.PasswordHash.Should().NotBeNullOrEmpty();
        dbUser.PasswordHash.Should().NotBe("Qwer1234!");
    }

    [Fact]
    public async Task Should_return_409_when_email_already_registered()
    {
        await SeedUser("taken@example.com");

        var request = new RegisterRequestContract("Taken@Example.com", "Other", "Qwer1234!");

        var response = await Act(request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsContract>();
        problem.Should().NotBeNull();
        problem!.ErrorCode.Should().Be("auth:user:email:already_exists");
    }

    [Fact]
    public async Task Should_return_400_for_invalid_input()
    {
        var request = new RegisterRequestContract("not-an-email", "", "short");

        var response = await Act(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsContract>();
        problem.Should().NotBeNull();
        problem!.Errors.Should().NotBeNullOrEmpty();
    }

    private async Task SeedUser(string email)
    {
        var hash = await _fixture.WithService<IPasswordHasher, string>(
            h => Task.FromResult(h.Hash("Qwer1234!")));

        await _fixture.Database.Save(new User
        {
            Id = 1,
            Email = email,
            Name = "Seeded",
            PasswordHash = hash,
            CreatedAt = DateTime.UtcNow,
        });
    }

    public class ValidatorTests
    {
        private readonly Register.RequestValidator _validator = new();

        [Theory]
        [InlineData("")]
        [InlineData("not-an-email")]
        [InlineData("missing-at-sign.com")]
        public void Should_reject_invalid_email(string email)
        {
            var result = _validator.TestValidate(
                new Register.Request(email, "Sam", "Qwer1234!"));
            result.ShouldHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public void Should_accept_valid_email()
        {
            var result = _validator.TestValidate(
                new Register.Request("sam@example.com", "Sam", "Qwer1234!"));
            result.ShouldNotHaveValidationErrorFor(x => x.Email);
        }

        [Fact]
        public void Should_reject_empty_name()
        {
            var result = _validator.TestValidate(
                new Register.Request("sam@example.com", "", "Qwer1234!"));
            result.ShouldHaveValidationErrorFor(x => x.Name);
        }

        [Fact]
        public void Should_reject_name_longer_than_100_chars()
        {
            var result = _validator.TestValidate(
                new Register.Request("sam@example.com", new string('a', 101), "Qwer1234!"));
            result.ShouldHaveValidationErrorFor(x => x.Name);
        }

        [Fact]
        public void Should_accept_name_at_100_chars()
        {
            var result = _validator.TestValidate(
                new Register.Request("sam@example.com", new string('a', 100), "Qwer1234!"));
            result.ShouldNotHaveValidationErrorFor(x => x.Name);
        }

        [Theory]
        [InlineData("")]
        [InlineData("short")]
        [InlineData("1234567")]
        public void Should_reject_password_shorter_than_8_chars(string password)
        {
            var result = _validator.TestValidate(
                new Register.Request("sam@example.com", "Sam", password));
            result.ShouldHaveValidationErrorFor(x => x.Password);
        }

        [Fact]
        public void Should_accept_password_at_8_chars()
        {
            var result = _validator.TestValidate(
                new Register.Request("sam@example.com", "Sam", "12345678"));
            result.ShouldNotHaveValidationErrorFor(x => x.Password);
        }
    }
}

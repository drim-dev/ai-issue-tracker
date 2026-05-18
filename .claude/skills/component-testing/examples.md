# Примеры компонентных тестов

Этот документ содержит полные примеры тестов для различных сценариев.

## Полный пример тест-класса

```csharp
using System.Net;
using FluentAssertions;
using FluentAssertions.Extensions;
using RestSharp;
using YourApp.Domain;
using YourApp.Features.Auth;
using YourApp.Tests.Contracts;
using YourApp.Tests.Extensions;
using YourApp.Tests.Fixtures;

namespace YourApp.Tests.Features.Auth;

[Collection(AuthTestsCollection.Name)]
public class CreateAccountTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CreateAccountTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<RestResponse<T>> Act<T>(CreateAccountRequestContract request)
    {
        var client = new RestClient(_fixture.HttpClient.CreateClient());
        return await client.ExecutePostAsync<T>(
            "/auth/accounts",
            request,
            CreateCancellationToken());
    }

    [Fact]
    public async Task Should_create_account()
    {
        // Arrange
        const string login = "Sam";
        var request = new CreateAccountRequestContract(login, "Qwer1234!");

        // Act
        var restResponse = await Act<AccountContract>(request);

        // Assert HTTP-ответ
        restResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var responseAccount = restResponse.Data;
        responseAccount.ShouldNotBeNull();

        restResponse.Headers.Location().Should().Be($"/auth/accounts/{responseAccount.Login}");

        responseAccount.Login.Should().Be(login.ToLower());
        responseAccount.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, 1.Seconds());

        // Assert состояние БД через harness
        var dbAccount = await _fixture.Database.SingleOrDefault<Account>(
            x => x.Login == responseAccount.Login,
            CreateCancellationToken());

        dbAccount.ShouldNotBeNull();
        dbAccount.Id.Should().BeGreaterOrEqualTo(0);
        dbAccount.Login.Should().Be(login.ToLower());
        dbAccount.CreatedAt.Should().BeCloseTo(responseAccount.CreatedAt, 100.Microseconds());
        dbAccount.PasswordHash.Should().NotBeEmpty();
        dbAccount.PasswordHash.Split('$').Should().HaveCount(6);
    }

    [Theory]
    [InlineData("sam")]
    [InlineData("Sam")]
    public async Task Should_return_conflict_if_account_exists_case_insensitive(string login)
    {
        // Arrange
        await _fixture.Database.Save(CreateAccount(login));

        var request = new CreateAccountRequestContract(login, "Qwer1234!");

        // Act
        var restResponse = await Act<ProblemDetailsContract>(request);

        // Assert
        restResponse.ShouldBeLogicConflictError(
            "Account already exists",
            "auth:logic:account_already_exists");
    }

    [Theory]
    [InlineData("ab", "Qwer1234!")]
    [InlineData("Sam", "weak")]
    public async Task Should_return_validation_error_for_invalid_input(string login, string password)
    {
        // Arrange
        var request = new CreateAccountRequestContract(login, password);

        // Act
        var restResponse = await Act<ProblemDetailsContract>(request);

        // Assert
        restResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        restResponse.Data.Should().NotBeNull();
        restResponse.Data.Errors.Should().NotBeEmpty();
    }
}
```

## Тестирование с аутентификацией

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using YourApp.Domain;
using YourApp.Features.Posts;
using YourApp.Tests.Fixtures;

namespace YourApp.Tests.Features.Posts;

[Collection(PostsTestsCollection.Name)]
public class UpdatePostTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public UpdatePostTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Should_update_own_post()
    {
        // Arrange
        var (client, account) = await _fixture.CreateAuthedHttpClient();
        var post = CreatePost(authorId: account.Id, slug: "my-post");
        await _fixture.Database.Save(post);

        var request = new UpdatePostRequestContract(
            Title: "Updated Title",
            Content: "Updated content");

        // Act
        var response = await client.PutAsJsonAsync(
            $"/posts/{post.Slug}",
            request,
            CreateCancellationToken());

        // Assert HTTP-ответ
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert состояние БД
        var updatedPost = await _fixture.Database.SingleOrDefault<Post>(
            x => x.Slug == post.Slug,
            CreateCancellationToken());

        updatedPost.Should().NotBeNull();
        updatedPost.Title.Should().Be("Updated Title");
        updatedPost.Content.Should().Be("Updated content");
    }

    [Fact]
    public async Task Should_not_update_others_post()
    {
        // Arrange
        var account1 = CreateAccount(login: "user1");
        var account2 = CreateAccount(login: "user2");
        await _fixture.Database.Save(account1, account2);

        var post = CreatePost(authorId: account1.Id, slug: "user1-post");
        await _fixture.Database.Save(post);

        var jwt = await _fixture.WithService<JwtGenerator>(gen => gen.Generate(account2));
        var client = _fixture.HttpClient.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", jwt);

        var request = new UpdatePostRequestContract(
            Title: "Hacked Title",
            Content: "Hacked content");

        // Act
        var response = await client.PutAsJsonAsync(
            $"/posts/{post.Slug}",
            request,
            CreateCancellationToken());

        // Assert — Forbidden
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Assert БД не изменилась
        var unchangedPost = await _fixture.Database.SingleOrDefault<Post>(
            x => x.Slug == post.Slug,
            CreateCancellationToken());

        unchangedPost.Should().NotBeNull();
        unchangedPost.Title.Should().Be(post.Title);
    }

    [Fact]
    public async Task Should_require_authentication()
    {
        // Arrange
        var post = CreatePost(slug: "test-post");
        await _fixture.Database.Save(post);

        var request = new UpdatePostRequestContract(
            Title: "Updated",
            Content: "Updated");

        // Act — без аутентификации
        var client = _fixture.HttpClient.CreateClient();
        var response = await client.PutAsJsonAsync(
            $"/posts/{post.Slug}",
            request,
            CreateCancellationToken());

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

## Тестирование ролевой авторизации

```csharp
using System.Net;
using FluentAssertions;
using YourApp.Domain;
using YourApp.Tests.Fixtures;

namespace YourApp.Tests.Features.Admin;

[Collection(AdminTestsCollection.Name)]
public class DeleteUserTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public DeleteUserTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Admin_should_delete_user()
    {
        // Arrange
        var (adminClient, _) = await _fixture.CreateAdminHttpClient();

        var targetUser = CreateAccount(login: "targetuser");
        await _fixture.Database.Save(targetUser);

        // Act
        var response = await adminClient.DeleteAsync(
            $"/admin/users/{targetUser.Id}",
            CreateCancellationToken());

        // Assert HTTP-ответ
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Assert пользователь удалён из БД
        var deletedUser = await _fixture.Database.SingleOrDefault<Account>(
            x => x.Id == targetUser.Id,
            CreateCancellationToken());

        deletedUser.Should().BeNull();
    }

    [Fact]
    public async Task Regular_user_should_not_delete_user()
    {
        // Arrange
        var (client, _) = await _fixture.CreateAuthedHttpClient();

        var targetUser = CreateAccount(login: "targetuser");
        await _fixture.Database.Save(targetUser);

        // Act
        var response = await client.DeleteAsync(
            $"/admin/users/{targetUser.Id}",
            CreateCancellationToken());

        // Assert — Forbidden
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Assert пользователь всё ещё существует
        var existingUser = await _fixture.Database.SingleOrDefault<Account>(
            x => x.Id == targetUser.Id,
            CreateCancellationToken());

        existingUser.Should().NotBeNull();
    }
}
```

## Тестирование пагинации

```csharp
using System.Net.Http.Json;
using FluentAssertions;
using YourApp.Domain;
using YourApp.Features.Posts;
using YourApp.Tests.Fixtures;

namespace YourApp.Tests.Features.Posts;

[Collection(PostsTestsCollection.Name)]
public class ListPostsTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public ListPostsTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Should_return_first_page()
    {
        // Arrange
        var posts = Enumerable.Range(1, 25)
            .Select(i => CreatePost(slug: $"post-{i:D2}"))
            .ToList();
        await _fixture.Database.Save(posts);

        // Act
        var client = _fixture.HttpClient.CreateClient();
        var response = await client.GetFromJsonAsync<PostsListContract>(
            "/posts?page=1&pageSize=10",
            CreateCancellationToken());

        // Assert
        response.Should().NotBeNull();
        response.Posts.Should().HaveCount(10);
        response.TotalCount.Should().Be(25);
        response.Page.Should().Be(1);
        response.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task Should_return_empty_page_when_no_posts()
    {
        // Act
        var client = _fixture.HttpClient.CreateClient();
        var response = await client.GetFromJsonAsync<PostsListContract>(
            "/posts?page=1&pageSize=10",
            CreateCancellationToken());

        // Assert
        response.Should().NotBeNull();
        response.Posts.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
    }
}
```

## Хелпер-методы в тестовых классах

```csharp
[Collection(PostsTestsCollection.Name)]
public class PostTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public PostTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<RestResponse<T>> CreatePost<T>(CreatePostRequestContract request)
    {
        var client = new RestClient(_fixture.HttpClient.CreateClient());
        return await client.ExecutePostAsync<T>("/posts", request, CreateCancellationToken());
    }

    private Post CreatePost(
        string slug = "test-post",
        string title = "Test Post",
        string content = "Test content",
        long? authorId = null,
        bool isPublished = true)
    {
        return new Post(
            Id: 0,
            AuthorId: authorId ?? 1,
            Title: title,
            Slug: slug,
            Content: content,
            IsPublished: isPublished,
            CreatedAt: DateTime.UtcNow);
    }
}
```

## Резюме

**Структура компонентного теста:**
1. **Arrange** — подготовка данных через методы harness
2. **Act** — вызов HTTP-эндпоинта
3. **Assert** — проверка HTTP-ответа и побочных эффектов

**Типовые паттерны:**
- Сбрасывай fixture в `IAsyncLifetime.InitializeAsync()`
- Создавай хелпер-методы `Act<T>()`
- Используй хелпер-методы fixture для аутентификации
- Проверяй и ответ, и состояние БД
- Тестируй авторизацию и валидацию
- Используй harness для всех внешних зависимостей

# Справочник реализации TestFixture

Этот документ содержит полную реализацию TestFixture с хелпер-методами. TestFixture создаются один раз на xUnit-коллекцию и переиспользуются между несколькими классами тестов.

## Полный пример TestFixture

```csharp
using System.Net.Http.Headers;
using FluentAssertions;
using FluentAssertions.Extensions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using YourApp.Database;
using YourApp.Domain;
using YourApp.Features.Auth.Services;
using YourApp.Tests.Harnesses;

namespace YourApp.Tests.Fixtures;

public class TestFixture : IAsyncLifetime
{
    static TestFixture()
    {
        SetupFluentAssertions();
    }

    private readonly WebApplicationFactory<Program> _factory;

    public TestFixture()
    {
        Database = new DatabaseHarness<Program, AppDbContext>("DefaultConnection");
        HttpClient = new HttpClientHarness<Program>();

        _factory = new WebApplicationFactory<Program>()
            .AddHarness(Database)
            .AddHarness(HttpClient);
    }

    public WebApplicationFactory<Program> Factory => _factory;
    public DatabaseHarness<Program, AppDbContext> Database { get; }
    public HttpClientHarness<Program> HttpClient { get; }

    public async Task Reset(CancellationToken cancellationToken)
    {
        await Database.Clear(cancellationToken);
    }

    public async Task<(HttpClient, Account)> CreateAuthedHttpClient()
    {
        var account = CreateAccount();
        await Database.Save(account);

        await using var scope = _factory.Services.CreateAsyncScope();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<JwtGenerator>();
        var jwt = jwtGenerator.Generate(account);

        var client = HttpClient.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", jwt);

        return (client, account);
    }

    public async Task<(HttpClient, Account)> CreateWronglyAuthedHttpClient()
    {
        var account = CreateAccount();
        await Database.Save(account);

        await using var scope = _factory.Services.CreateAsyncScope();
        var jwtGenerator = scope.ServiceProvider.GetRequiredService<JwtGenerator>();
        var incorrectJwt = jwtGenerator.Generate(account) + "123";

        var client = HttpClient.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", incorrectJwt);

        return (client, account);
    }

    public async Task InitializeAsync()
    {
        await Database.Start(_factory, CreateCancellationToken(60));
        await HttpClient.Start(_factory, CreateCancellationToken());

        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        await HttpClient.Stop(CreateCancellationToken());
        await Database.Stop(CreateCancellationToken());
    }

    // Обходное решение проблемы конкурентности FluentAssertions
    // https://github.com/fluentassertions/fluentassertions/issues/1932#issuecomment-1137366562
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void SetupFluentAssertions()
    {
        AssertionOptions.AssertEquivalencyUsing(options => options
            .Using<DateTimeOffset>(ctx => ctx.Subject.Should().BeSameDateAs(ctx.Expectation))
            .WhenTypeIs<DateTimeOffset>()
            .Using<DateTime>(ctx => ctx.Subject.Should().BeSameDateAs(ctx.Expectation))
            .WhenTypeIs<DateTime>()
        );
    }
}
```

## TestFixture с несколькими базами данных

Если приложение использует несколько баз данных:

```csharp
public class TestFixture : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;

    public TestFixture()
    {
        AuthDb = new DatabaseHarness<Program, AuthDbContext>("AuthConnection");
        ContentDb = new DatabaseHarness<Program, ContentDbContext>("ContentConnection");
        HttpClient = new HttpClientHarness<Program>();

        _factory = new WebApplicationFactory<Program>()
            .AddHarness(AuthDb)
            .AddHarness(ContentDb)
            .AddHarness(HttpClient);
    }

    public DatabaseHarness<Program, AuthDbContext> AuthDb { get; }
    public DatabaseHarness<Program, ContentDbContext> ContentDb { get; }
    public HttpClientHarness<Program> HttpClient { get; }

    public async Task Reset(CancellationToken cancellationToken)
    {
        await AuthDb.Clear(cancellationToken);
        await ContentDb.Clear(cancellationToken);
    }

    public async Task InitializeAsync()
    {
        await AuthDb.Start(_factory, CreateCancellationToken(60));
        await ContentDb.Start(_factory, CreateCancellationToken(60));
        await HttpClient.Start(_factory, CreateCancellationToken());
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        await HttpClient.Stop(CreateCancellationToken());
        await AuthDb.Stop(CreateCancellationToken());
        await ContentDb.Stop(CreateCancellationToken());
    }
}
```

## TestFixture с дополнительными Harness

Пример с Redis и Kafka:

```csharp
public class TestFixture : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;

    public TestFixture()
    {
        Database = new DatabaseHarness<Program, AppDbContext>("DefaultConnection");
        Redis = new RedisHarness<Program>();
        Kafka = new KafkaHarness<Program>();
        HttpClient = new HttpClientHarness<Program>();

        _factory = new WebApplicationFactory<Program>()
            .AddHarness(Database)
            .AddHarness(Redis)
            .AddHarness(Kafka)
            .AddHarness(HttpClient);
    }

    public DatabaseHarness<Program, AppDbContext> Database { get; }
    public RedisHarness<Program> Redis { get; }
    public KafkaHarness<Program> Kafka { get; }
    public HttpClientHarness<Program> HttpClient { get; }

    public async Task Reset(CancellationToken cancellationToken)
    {
        await Database.Clear(cancellationToken);
        await Redis.Clear();
    }

    public async Task InitializeAsync()
    {
        await Database.Start(_factory, CreateCancellationToken(60));
        await Redis.Start(_factory, CreateCancellationToken(30));
        await Kafka.Start(_factory, CreateCancellationToken(60));
        await HttpClient.Start(_factory, CreateCancellationToken());
        _ = _factory.Server;
    }

    public async Task DisposeAsync()
    {
        await HttpClient.Stop(CreateCancellationToken());
        await Database.Stop(CreateCancellationToken());
        await Redis.Stop(CreateCancellationToken());
        await Kafka.Stop(CreateCancellationToken());
    }
}
```

## Типовые хелпер-методы

### Хелперы аутентификации

```csharp
public async Task<(HttpClient, Account)> CreateAuthedHttpClient()
{
    var account = CreateAccount();
    await Database.Save(account);

    var jwt = GenerateJwt(account);

    var client = HttpClient.CreateClient();
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", jwt);

    return (client, account);
}

public async Task<(HttpClient, Account)> CreateAdminHttpClient()
{
    var account = CreateAccount(role: "Admin");
    await Database.Save(account);

    var jwt = GenerateJwt(account);

    var client = HttpClient.CreateClient();
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", jwt);

    return (client, account);
}

public async Task<(HttpClient, Account)> CreateExpiredTokenHttpClient()
{
    var account = CreateAccount();
    await Database.Save(account);

    var jwt = GenerateExpiredJwt(account);

    var client = HttpClient.CreateClient();
    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", jwt);

    return (client, account);
}

private string GenerateJwt(Account account)
{
    await using var scope = _factory.Services.CreateAsyncScope();
    var jwtGenerator = scope.ServiceProvider.GetRequiredService<JwtGenerator>();
    return jwtGenerator.Generate(account);
}
```

### Хелперы доступа к сервисам

```csharp
public T GetService<T>() where T : notnull
{
    var scope = _factory.Services.CreateScope();
    return scope.ServiceProvider.GetRequiredService<T>();
}

public async Task WithService<T>(Func<T, Task> action) where T : notnull
{
    await using var scope = _factory.Services.CreateAsyncScope();
    var service = scope.ServiceProvider.GetRequiredService<T>();
    await action(service);
}
```

## Настройка xUnit-коллекции

Каждому TestFixture нужна соответствующая xUnit-коллекция:

```csharp
using DrimAgents.Api.Tests.Fixtures;

namespace DrimAgents.Api.Tests.Features.Auth;

[CollectionDefinition(Name)]
public class AuthTestsCollection : ICollectionFixture<TestFixture>
{
    public const string Name = nameof(AuthTestsCollection);
}
```

**Зачем коллекции?**
- **Переиспользование fixture** — один экземпляр TestFixture разделяется между всеми тест-классами коллекции
- **Контроль параллелизма** — тесты одной коллекции выполняются последовательно
- **Производительность** — TestContainers запускаются один раз, а не на каждый тест-класс
- **Управление ресурсами** — контейнеры очищаются один раз при завершении коллекции

## Использование TestFixture в тестах

```csharp
using YourApp.Tests.Fixtures;

[Collection(AuthTestsCollection.Name)]
public class CreateAccountTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CreateAccountTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Should_create_account()
    {
        var (client, account) = await _fixture.CreateAuthedHttpClient();
        // логика теста...
    }
}
```

## Жизненный цикл Fixture

1. **Старт коллекции** — конструктор TestFixture выполняется один раз
2. **InitializeAsync** — запуск всех harness (запуск TestContainers)
3. **Тест-класс 1** — использует fixture
   - **IAsyncLifetime.InitializeAsync** — сброс состояния fixture
   - **Тест 1** — выполняется
   - **Тест 2** — выполняется
   - **IAsyncLifetime.DisposeAsync** — no-op
4. **Тест-класс 2** — использует тот же fixture
   - **IAsyncLifetime.InitializeAsync** — сброс состояния fixture
   - **Тест 3** — выполняется
   - **IAsyncLifetime.DisposeAsync** — no-op
5. **Конец коллекции** — TestFixture.DisposeAsync (остановка контейнеров)

## Лучшие практики

### НАДО: Использовать Reset() между тестами
```csharp
public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());
```
**Почему:** Быстрая очистка с Respawn (сохраняет схему, очищает данные)

### НАДО: Создавать хелпер-методы для типовых сценариев
```csharp
public async Task<(HttpClient, Account)> CreateAuthedHttpClient() { }
public async Task<(HttpClient, Account)> CreateAdminHttpClient() { }
```
**Почему:** Уменьшает дублирование, делает тесты читабельнее

### НАДО: Предоставлять harness как свойства
```csharp
public DatabaseHarness<Program, WebApiDbContext> Database { get; }
public HttpClientHarness<Program> HttpClient { get; }
```
**Почему:** Чёткий, типобезопасный доступ к методам harness

### НЕ НАДО: Запускать/останавливать контейнеры на каждый тест
```csharp
// ПЛОХО — слишком медленно
public async Task InitializeAsync()
{
    await _fixture.Database.Start(...);
}
```
**Почему:** Запуск TestContainers медленный. Запускай один раз на коллекцию, сбрасывай между тестами.

### НЕ НАДО: Делиться мутабельным состоянием между тестами
```csharp
// ПЛОХО — состояние утекает между тестами
private readonly Account _sharedAccount = CreateAccount();
```
**Почему:** Тесты должны быть независимыми. Используй `Reset()` для очистки, создавай свежие сущности в каждом тесте.

## Устранение неполадок

### TestContainers медленные

**Решение:** Убедись, что контейнеры переиспользуются:
- Используй xUnit-коллекции для переиспользования TestFixture
- Вызывай `Reset()` между тестами вместо рестарта
- Используй быстрые методы очистки (Respawn для БД, FlushDatabase для Redis)

### Тесты падают нестабильно

**Решение:** Обеспечь правильную очистку:
- Вызывай `Reset()` в `IAsyncLifetime.InitializeAsync()`
- Проверь, что конфигурация Respawn включает все схемы
- Проверь, что фоновые задачи или асинхронные операции завершены

### Out of memory

**Решение:** Ограничь количество параллельных тестовых коллекций:
- xUnit по умолчанию запускает коллекции параллельно
- Установи `maxParallelThreads` в xunit.runner.json при необходимости
- Убедись, что TestContainers правильно уничтожаются

## Резюме

**Ответственности TestFixture:**
1. Инициализация harness один раз на коллекцию
2. Предоставление harness для доступа тестов
3. Хелпер-методы для типовых сценариев
4. Быстрый сброс между тестами
5. Очистка при завершении коллекции

**Ключевые паттерны:**
- Используй `IAsyncLifetime` для жизненного цикла fixture
- Создавай xUnit-коллекцию для переиспользования
- Сбрасывай в `IAsyncLifetime.InitializeAsync()` теста
- Предоставляй harness как свойства
- Добавляй доменные хелпер-методы

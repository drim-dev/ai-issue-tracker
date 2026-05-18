---
name: component-testing
description: Используй при написании тестов для вертикальных слайсов — предоставляет паттерны компонентного тестирования на основе harness, которые тестируют фичи целиком через HTTP-эндпоинты с реальными зависимостями через TestContainers
---

# Компонентное тестирование с Harness

## Когда использовать этот скилл

Используй этот скилл когда:

- Пишешь тесты для фич вертикальных слайсов
- Нужно тестировать фичу как целое (а не отдельные классы)
- Работаешь с внешними зависимостями (БД, Redis, Kafka, файловое хранилище и т.д.)
- Настраиваешь интеграционные тесты, которые легко поддерживать при рефакторинге

**Объяви в начале:** "Я использую скилл component-testing для написания тестов для этой фичи."

## Философия

**Компонентное тестирование** означает тестирование фичи вертикального слайса как целостной единицы, включая все её внутренние классы и логику, с контролем внешних зависимостей через harness.

**Преимущества:**

- **Лёгкий рефакторинг** — внутренние изменения не ломают тесты
- **Реалистичное поведение** — тесты на реальных зависимостях (PostgreSQL, Redis, Kafka)
- **Поддерживаемость** — тесты фокусируются на поведении, а не деталях реализации
- **Гибкость** — можно менять реализации без изменения тестов
- **Быстрая обратная связь** — TestContainers обеспечивают быстрый setup/teardown

**Не unit-тесты:** Мы не тестируем отдельные классы. Мы тестируем всю фичу через её HTTP-эндпоинт.

## Предпочтение TestContainers

**ВАЖНО: Используй TestContainers для реальных зависимостей по возможности.**

**Предпочитай (в порядке приоритета):**

1. **TestContainers с реальной зависимостью** (контейнеры PostgreSQL, Redis, Kafka)
2. **Лёгкие альтернативы** (SQLite вместо PostgreSQL, in-memory cache)
3. **Моки** (только когда TestContainers непрактичны)

**Почему TestContainers?**

- **Реальное поведение** — тесты на реальной БД, а не in-memory симуляции
- **Обнаружение интеграционных проблем** — различия в диалектах SQL, пулы соединений и т.д.
- **Приближённость к продакшену** — тот же движок БД, что и на проде
- **Достаточно быстро** — переиспользование контейнеров в рамках коллекции
- **Без сюрпризов** — что работает в тестах, работает в продакшене

**Когда использовать альтернативы:**

- **Ограничения производительности** — если TestContainers слишком медленные (редко при правильной настройке fixture)
- **Ограничения CI** — если CI-окружение не поддерживает Docker (редко)
- **Внешние API** — используй WireMock или моки для сторонних API

## Что такое Harness?

**Harness** — это абстракция внешней зависимости, инкапсулирующая:

- Запуск зависимости (TestContainer, мок и т.д.)
- Конфигурацию SUT для её использования
- Подготовку данных для тестов
- Проверку состояния после операций
- Очистку между тестами

### Ответственности Harness

1. **Start** — запуск TestContainer или инициализация мока
2. **Configure** — переопределение строк подключения, регистрация в DI
3. **Seed** — предоставление методов для подготовки тестовых данных
4. **Assert** — предоставление методов для проверки результатов
5. **Stop** — очистка ресурсов

## Базовые интерфейсы

### Интерфейс IHarness

```csharp
public interface IHarness<T> where T : class
{
    void ConfigureWebHostBuilder(IWebHostBuilder builder);
    Task Start(WebApplicationFactory<T> factory, CancellationToken cancellationToken);
    Task Stop(CancellationToken cancellationToken);
}
```

### Метод расширения

```csharp
public static class HarnessExtensions
{
    public static WebApplicationFactory<T> AddHarness<T>(
        this WebApplicationFactory<T> factory,
        IHarness<T> harness)
        where T : class =>
        factory.WithWebHostBuilder(harness.ConfigureWebHostBuilder);
}
```

## Быстрый старт

### 1. Создание реализаций Harness

**Полные реализации смотри в [harnesses.md](harnesses.md):**

- DatabaseHarness с PostgreSQL TestContainer
- HttpClientHarness для HTTP-запросов
- Другие типы harness (Redis, Kafka и т.д.)

### 2. Создание TestFixture

**Полную реализацию смотри в [test-fixture.md](test-fixture.md).**

Краткий обзор:

```csharp
public class TestFixture : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;

    public TestFixture()
    {
        Database = new DatabaseHarness<Program, AppDbContext>("DefaultConnection");
        HttpClient = new HttpClientHarness<Program>();

        _factory = new WebApplicationFactory<Program>()
            .AddHarness(Database)
            .AddHarness(HttpClient);
    }

    public DatabaseHarness<Program, AppDbContext> Database { get; }
    public HttpClientHarness<Program> HttpClient { get; }

    public async Task Reset(CancellationToken cancellationToken) =>
        await Database.Clear(cancellationToken);

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
}
```

### 3. Создание xUnit-коллекции (доменной)

**ВАЖНО: Создавай доменные коллекции, НЕ общие.**

```csharp
// ХОРОШО — доменная коллекция
[CollectionDefinition(Name)]
public class SkillsTestsCollection : ICollectionFixture<TestFixture>
{
    public const string Name = nameof(SkillsTestsCollection);
}

// ХОРОШО — другая доменная коллекция
[CollectionDefinition(Name)]
public class BlogTestsCollection : ICollectionFixture<TestFixture>
{
    public const string Name = nameof(BlogTestsCollection);
}

// ПЛОХО — общее название коллекции
[CollectionDefinition(Name)]
public class DatabaseCollection : ICollectionFixture<TestFixture>
{
    public const string Name = nameof(DatabaseCollection);
}
```

**Правила именования коллекций:**

- Называй коллекции по **домену/области фич** (Skills, Blog, Courses, Users и т.д.)
- Используй паттерн: `{Domain}TestsCollection` (например, SkillsTestsCollection, BlogTestsCollection)
- НЕ используй общие названия вроде DatabaseCollection, ApiCollection, TestCollection
- НЕ создавай одну коллекцию для всех тестов

**Почему доменные коллекции?**

- **Параллельное выполнение** — разные домены могут работать параллельно (SkillsTests || BlogTests)
- **Изоляция** — тестовые данные домена не мешают другим коллекциям
- **Чёткая организация** — тесты сгруппированы по доменам, соответствуя vertical slice архитектуре
- **Производительность** — TestContainers переиспользуются внутри домена, но домены работают параллельно
- **Гибкость** — некоторым доменам могут потребоваться разные конфигурации harness

**Зачем нужны коллекции:**

- Переиспользование одного TestFixture (и TestContainers) между несколькими классами тестов одного домена
- Контроль параллелизма (тесты в одной коллекции работают последовательно, разные коллекции — параллельно)
- Амортизация стоимости запуска TestContainers внутри домена

### 4. Написание компонентных тестов

```csharp
[Collection(UsersTestsCollection.Name)]
public class CreateAccountTests : IAsyncLifetime
{
    private readonly TestFixture _fixture;

    public CreateAccountTests(TestFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.Reset(CreateCancellationToken());
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Should_create_account()
    {
        // Arrange
        const string login = "Sam";
        var request = new CreateAccountRequestContract(login, "Qwer1234!");

        // Act
        var client = new RestClient(_fixture.HttpClient.CreateClient());
        var response = await client.ExecutePostAsync<AccountContract>(
            "/auth/accounts", request, CreateCancellationToken());

        // Assert HTTP-ответ
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location().Should().Be($"/auth/accounts/{login.ToLower()}");
        response.Data.Login.Should().Be(login.ToLower());

        // Assert состояние БД (через harness)
        var dbAccount = await _fixture.Database.SingleOrDefault<Account>(
            x => x.Login == login.ToLower(),
            CreateCancellationToken());

        dbAccount.Should().NotBeNull();
        dbAccount.PasswordHash.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Should_return_conflict_if_account_exists()
    {
        // Arrange
        await _fixture.Database.Save(CreateAccount("sam"));

        var request = new CreateAccountRequestContract("Sam", "Qwer1234!");

        // Act
        var client = new RestClient(_fixture.HttpClient.CreateClient());
        var response = await client.ExecutePostAsync<ProblemDetailsContract>(
            "/auth/accounts", request, CreateCancellationToken());

        // Assert
        response.ShouldBeLogicConflictError(
            "Account already exists",
            "auth:logic:account_already_exists");
    }
}
```

**Больше примеров смотри в [examples.md](examples.md).**

## Паттерны компонентных тестов

### Структура теста (Arrange-Act-Assert)

1. **Arrange** — подготовка через методы harness
   ```csharp
   await _fixture.Database.Save(account, post);
   ```

2. **Act** — вызов HTTP-эндпоинта (тестирует весь вертикальный слайс)
   ```csharp
   var response = await client.ExecutePostAsync<Result>("/endpoint", request);
   ```

3. **Assert HTTP-ответ** — статус-код, заголовки, тело
   ```csharp
   response.StatusCode.Should().Be(HttpStatusCode.Created);
   response.Data.Should().NotBeNull();
   ```

4. **Assert побочные эффекты** — состояние БД, отправленные сообщения и т.д.
   ```csharp
   var entity = await _fixture.Database.SingleOrDefault<Entity>(x => x.Id == id);
   entity.Should().NotBeNull();
   ```

### Что НЕ тестировать в компонентных тестах

**КРИТИЧНО: НИКОГДА не тестируй правила валидации в компонентных тестах.**

**НЕ создавай компонентные тесты для сценариев валидации.**

Правила валидации тестируются в изолированных unit-тестах с помощью FluentValidation.TestHelper. Компонентные тесты фокусируются на бизнес-логике, авторизации и побочных эффектах — НЕ на валидации.

**Если у фичи есть RequestValidator:**
1. Создай класс `ValidatorTests`, вложенный в файл компонентных тестов
2. Тестируй ВСЕ правила валидации через FluentValidation.TestHelper
3. Компонентные тесты НЕ должны тестировать ошибки валидации (пустые поля, макс. длины, невалидные форматы и т.д.)

**Пример того, чего НЕ надо делать:**

```csharp
// ПЛОХО — валидатор уже протестирован в ValidatorTests
[Fact]
public async Task Should_return_validation_error_when_display_name_too_long()
{
    var request = new { DisplayName = new string('A', 101) };
    var response = await client.PatchAsJsonAsync("/api/users/me", request);
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

**Что тестировать в компонентных тестах:**
- Бизнес-логику и поведение фичи
- Авторизацию (403 Forbidden, 401 Unauthorized)
- Существование ресурса (404 Not Found)
- Конфликты (409 Conflict)
- Побочные эффекты в БД
- Успешные сценарии с валидными данными

### Подготовка данных

```csharp
await _fixture.Database.Save(account);
await _fixture.Database.Save(account1, account2, post);
await _fixture.Database.Save(accountsList, postsList);

await _fixture.Database.Execute(async db =>
{
    db.Accounts.AddRange(accounts);
    await db.SaveChangesAsync();
});
```

### Проверка состояния

```csharp
var account = await _fixture.Database.SingleOrDefault<Account>(
    x => x.Login == "sam",
    cancellationToken);

var count = await _fixture.Database.Execute(async db =>
    await db.Accounts.CountAsync());
```

### Тестирование с аутентификацией

```csharp
[Fact]
public async Task Should_require_authentication()
{
    var (client, account) = await _fixture.CreateAuthedHttpClient();

    var response = await client.GetAsync("/protected-resource");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
}
```

## Типовые сценарии

### Тестирование авторизации

```csharp
[Fact]
public async Task Should_require_admin_role()
{
    var (client, account) = await _fixture.CreateAuthedHttpClient();

    var response = await client.DeleteAsync("/admin/users/123");

    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

### Тестирование ошибок бизнес-логики

```csharp
[Fact]
public async Task Should_return_conflict_for_duplicate()
{
    await _fixture.Database.Save(CreatePost("my-slug"));

    var request = new CreatePostRequest("My Post", "my-slug", "Content");

    var response = await Act<ProblemDetailsContract>(request);

    response.ShouldBeLogicConflictError("Post with this slug already exists");
}
```

## Рекомендации по разработке Harness

### Создание нового Harness

1. **Реализуй IHarness<T>**
2. **Используй TestContainers** для реальной зависимости (предпочтительно)
3. **ConfigureWebHostBuilder** — переопредели строки подключения или настройки
4. **Start** — запусти TestContainer
5. **Stop** — очисти TestContainer
6. **Добавь методы подготовки данных** — настройка тестовых данных
7. **Добавь методы проверки** — запрос состояния для верификации
8. **Добавь метод очистки** — быстрый сброс между тестами (например, Respawn для БД)

### Пример: Redis Harness с TestContainers

```csharp
public class RedisHarness<TProgram> : IHarness<TProgram>
    where TProgram : class
{
    private RedisContainer? _redis;

    public void ConfigureWebHostBuilder(IWebHostBuilder builder)
    {
        builder.UseSetting("Redis:ConnectionString", _redis!.GetConnectionString());
    }

    public async Task Start(WebApplicationFactory<TProgram> factory, CancellationToken ct)
    {
        _redis = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _redis.StartAsync(ct);
    }

    public async Task Stop(CancellationToken ct)
    {
        if (_redis is not null)
        {
            await _redis.StopAsync(ct);
            await _redis.DisposeAsync();
        }
    }

    public async Task Set<T>(string key, T value) { /* ... */ }
    public async Task<T?> Get<T>(string key) { /* ... */ }
    public async Task Clear() { /* ... */ }
}
```

## Рабочий процесс тестирования (TDD)

1. **Red** — напиши падающий компонентный тест
   - Определи контракт HTTP запрос/ответ
   - Определи ожидаемые побочные эффекты (состояние БД и т.д.)

2. **Green** — реализуй вертикальный слайс
   - Создай Endpoint, Request, Validator, Handler
   - Запускай тест, пока не пройдёт

3. **Refactor** — улучши реализацию
   - Тесты остаются зелёными (они тестируют поведение, а не реализацию)

## Справочная документация

- **[harnesses.md](harnesses.md)** — полные реализации harness (DatabaseHarness, HttpClientHarness и т.д.)
- **[test-fixture.md](test-fixture.md)** — полная реализация TestFixture с хелперами
- **[examples.md](examples.md)** — полные примеры тестов для различных сценариев

## Чеклист тестирования

При написании компонентных тестов:

- [ ] Создать harness для внешних зависимостей (предпочтительно TestContainers)
- [ ] Настроить TestFixture со всеми harness
- [ ] Создать xUnit-коллекцию для переиспользования fixture
- [ ] Сбрасывать fixture в `IAsyncLifetime.InitializeAsync()`
- [ ] Тестировать через HTTP-эндпоинт (весь вертикальный слайс)
- [ ] Проверять и HTTP-ответ, и побочные эффекты
- [ ] Использовать методы подготовки данных harness для arrange
- [ ] Использовать методы проверки harness для verify
- [ ] Держать тесты сфокусированными на поведении фичи
- [ ] Добавить хелпер-методы fixture для типовых сценариев

## Резюме

**Компонентные тесты проверяют, что фичи вертикальных слайсов работают корректно как целое:**
- Тестируй через HTTP-эндпоинт (реалистично)
- Используй TestContainers для реальных зависимостей (предпочтительно)
- Используй harness для абстрагирования настройки зависимостей
- Сбрасывай состояние между тестами (быстро с Respawn)
- Проверяй и ответ, и побочные эффекты

**Помни:** TestContainers обеспечивают наиболее реалистичную среду тестирования. Откатывайся к мокам только когда это действительно необходимо.

# Справочник реализаций Harness

Этот документ содержит полные реализации harness. Они создаются один раз и переиспользуются во всех тестах.

## DatabaseHarness (PostgreSQL с TestContainers)

Полная реализация с методами подготовки данных, проверки и очистки:

```csharp
using System.Collections;
using System.Linq.Expressions;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;

namespace YourApp.Tests.Harnesses;

public class DatabaseHarness<TProgram, TDbContext> : IHarness<TProgram>
    where TProgram : class
    where TDbContext : DbContext
{
    private PostgreSqlContainer? _postgres;
    private WebApplicationFactory<TProgram>? _factory;
    private bool _started;
    private readonly string _databaseResourceName;

    public DatabaseHarness(string databaseResourceName)
    {
        _databaseResourceName = databaseResourceName;
    }

    public void ConfigureWebHostBuilder(IWebHostBuilder builder)
    {
        builder.UseSetting(
            $"ConnectionStrings:{_databaseResourceName}",
            _postgres!.GetConnectionString());
    }

    public async Task Start(WebApplicationFactory<TProgram> factory, CancellationToken cancellationToken)
    {
        _factory = factory;

        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:15.4-alpine3.18")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
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

    // === МЕТОДЫ ПОДГОТОВКИ ДАННЫХ ===

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
        await using var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var collections = entities.OfType<IEnumerable>();
        foreach (var collection in collections)
        {
            dbContext.AddRange(collection);
        }

        var singleEntities = entities.Where(e => e is not IEnumerable);
        dbContext.AddRange(singleEntities);

        await dbContext.SaveChangesAsync();
    }

    public async Task Execute(Func<TDbContext, Task> action)
    {
        ThrowIfNotStarted();

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await action(db);
    }

    public async Task<T> Execute<T>(Func<TDbContext, Task<T>> action)
    {
        ThrowIfNotStarted();

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        return await action(db);
    }

    // === МЕТОДЫ ПРОВЕРКИ ===

    public async Task<T?> SingleOrDefault<T>(
        Expression<Func<T, bool>> predicate,
        CancellationToken cancellationToken)
        where T : class
    {
        ThrowIfNotStarted();

        await using var scope = _factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        return await db.Set<T>().SingleOrDefaultAsync(predicate, cancellationToken);
    }

    // === МЕТОДЫ ОЧИСТКИ ===

    public async Task Clear(CancellationToken cancellationToken)
    {
        ThrowIfNotStarted();

        await using var connection = new NpgsqlConnection(_postgres!.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            SchemasToInclude = ["public"],
            DbAdapter = DbAdapter.Postgres,
        });

        await respawner.ResetAsync(connection);
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
```

## HttpClientHarness

Простой harness для создания HTTP-клиентов для вызова API:

```csharp
namespace YourApp.Tests.Harnesses;

public class HttpClientHarness<TProgram> : IHarness<TProgram>
    where TProgram : class
{
    private WebApplicationFactory<TProgram>? _factory;

    public void ConfigureWebHostBuilder(IWebHostBuilder builder)
    {
    }

    public Task Start(WebApplicationFactory<TProgram> factory, CancellationToken cancellationToken)
    {
        _factory = factory;
        return Task.CompletedTask;
    }

    public Task Stop(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public HttpClient CreateClient()
    {
        if (_factory is null)
        {
            throw new InvalidOperationException(
                $"HttpClient harness is not started. Call {nameof(Start)} first.");
        }

        return _factory.CreateClient();
    }
}
```

## RedisHarness (с TestContainers)

Пример harness для Redis с использованием TestContainers:

```csharp
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace YourApp.Tests.Harnesses;

public class RedisHarness<TProgram> : IHarness<TProgram>
    where TProgram : class
{
    private RedisContainer? _redis;
    private IConnectionMultiplexer? _connection;

    public void ConfigureWebHostBuilder(IWebHostBuilder builder)
    {
        builder.UseSetting("Redis:ConnectionString", _redis!.GetConnectionString());
    }

    public async Task Start(WebApplicationFactory<TProgram> factory, CancellationToken cancellationToken)
    {
        _redis = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();

        await _redis.StartAsync(cancellationToken);

        _connection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task Stop(CancellationToken cancellationToken)
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }

        if (_redis is not null)
        {
            await _redis.StopAsync(cancellationToken);
            await _redis.DisposeAsync();
        }
    }

    // === МЕТОДЫ ПОДГОТОВКИ ДАННЫХ ===

    public async Task Set<T>(string key, T value, TimeSpan? expiry = null)
    {
        var db = _connection!.GetDatabase();
        var json = JsonSerializer.Serialize(value);
        await db.StringSetAsync(key, json, expiry);
    }

    // === МЕТОДЫ ПРОВЕРКИ ===

    public async Task<T?> Get<T>(string key)
    {
        var db = _connection!.GetDatabase();
        var value = await db.StringGetAsync(key);

        return value.HasValue
            ? JsonSerializer.Deserialize<T>(value!)
            : default;
    }

    public async Task<bool> Exists(string key)
    {
        var db = _connection!.GetDatabase();
        return await db.KeyExistsAsync(key);
    }

    // === МЕТОДЫ ОЧИСТКИ ===

    public async Task Clear()
    {
        var endpoints = _connection!.GetEndPoints();
        var server = _connection.GetServer(endpoints.First());
        await server.FlushDatabaseAsync();
    }
}
```

## KafkaHarness (с TestContainers)

Пример harness для Kafka с использованием TestContainers:

```csharp
using System.Text.Json;
using Confluent.Kafka;
using DotNet.Testcontainers.Builders;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Testcontainers.Kafka;

namespace YourApp.Tests.Harnesses;

public class KafkaHarness<TProgram> : IHarness<TProgram>
    where TProgram : class
{
    private KafkaContainer? _kafka;
    private IProducer<string, string>? _producer;
    private IConsumer<string, string>? _consumer;

    public void ConfigureWebHostBuilder(IWebHostBuilder builder)
    {
        builder.UseSetting("Kafka:BootstrapServers", _kafka!.GetBootstrapAddress());
    }

    public async Task Start(WebApplicationFactory<TProgram> factory, CancellationToken cancellationToken)
    {
        _kafka = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:7.4.0")
            .Build();

        await _kafka.StartAsync(cancellationToken);

        var config = new ProducerConfig { BootstrapServers = _kafka.GetBootstrapAddress() };
        _producer = new ProducerBuilder<string, string>(config).Build();

        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _kafka.GetBootstrapAddress(),
            GroupId = "test-consumer",
            AutoOffsetReset = AutoOffsetReset.Earliest
        };
        _consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
    }

    public async Task Stop(CancellationToken cancellationToken)
    {
        _producer?.Dispose();
        _consumer?.Dispose();

        if (_kafka is not null)
        {
            await _kafka.StopAsync(cancellationToken);
            await _kafka.DisposeAsync();
        }
    }

    // === МЕТОДЫ ПОДГОТОВКИ ДАННЫХ ===

    public async Task Publish<T>(string topic, string key, T message)
    {
        var json = JsonSerializer.Serialize(message);
        await _producer!.ProduceAsync(topic, new Message<string, string>
        {
            Key = key,
            Value = json
        });
    }

    // === МЕТОДЫ ПРОВЕРКИ ===

    public async Task<List<T>> ConsumeMessages<T>(string topic, int expectedCount, TimeSpan timeout)
    {
        _consumer!.Subscribe(topic);

        var messages = new List<T>();
        var cts = new CancellationTokenSource(timeout);

        try
        {
            while (messages.Count < expectedCount && !cts.Token.IsCancellationRequested)
            {
                var result = _consumer.Consume(cts.Token);
                if (result != null)
                {
                    var message = JsonSerializer.Deserialize<T>(result.Message.Value);
                    if (message != null)
                    {
                        messages.Add(message);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }

        return messages;
    }
}
```

## Рекомендации по Harness

### При создании нового Harness

1. **Используй TestContainers** — всегда предпочитай TestContainers для реальных зависимостей
2. **Реализуй IHarness<T>** — следуй стандартному интерфейсу
3. **Переопредели конфигурацию** — используй `ConfigureWebHostBuilder` для указания на TestContainer
4. **Предоставь методы подготовки данных** — упрости настройку тестовых данных
5. **Предоставь методы проверки** — упрости верификацию результатов
6. **Предоставь метод очистки** — быстрый сброс между тестами (например, Respawn, FlushDatabase)

### Лучшие практики TestContainers

**Используй конкретные теги образов:**
```csharp
// Хорошо — воспроизводимо
.WithImage("postgres:15.4-alpine3.18")

// Плохо — непредсказуемо
.WithImage("postgres:latest")
```

**Используй стратегии ожидания:**
```csharp
.WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
```

**Переиспользуй контейнеры:**
- Контейнеры запускаются один раз на xUnit-коллекцию
- Используй методы `Reset()` или `Clear()` между тестами
- Не запускай/останавливай контейнеры на каждый тест (слишком медленно)

### Альтернатива: In-Memory для некритичных зависимостей

Если TestContainers слишком медленные или недоступны, используй in-memory альтернативы:

```csharp
public class InMemoryCacheHarness<TProgram> : IHarness<TProgram>
    where TProgram : class
{
    private readonly Dictionary<string, object> _cache = new();

    public void ConfigureWebHostBuilder(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(
                Options.Create(new MemoryDistributedCacheOptions())));
        });
    }

    public Task Start(WebApplicationFactory<TProgram> factory, CancellationToken ct) =>
        Task.CompletedTask;

    public Task Stop(CancellationToken ct) => Task.CompletedTask;
}
```

**Используй только когда:**
- Производительность TestContainers неприемлема
- CI-окружение не поддерживает Docker
- Тестирование некритичной логики кеширования

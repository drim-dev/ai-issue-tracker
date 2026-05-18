---
name: token-pagination
description: Используй при реализации list-эндпоинтов, возвращающих пагинированные результаты — предоставляет AIP-158-совместимую токенную пагинацию с шифрованными токенами и валидацией запросов (проект)
---

# Токенная пагинация (AIP-158)

Все list-эндпоинты в DrimAgents ОБЯЗАНЫ использовать токенную пагинацию по стандарту Google AIP-158.

## Когда использовать

Используй этот паттерн для **каждого эндпоинта, возвращающего список элементов**:

- Список навыков, курсов, постов блога
- Ленты активности пользователей
- Результаты поиска
- Админские таблицы
- Любой эндпоинт с коллекцией

## Параметры запроса

```csharp
app.MapGet("/posts", async Task<Ok<PageResponse<PostModel>>> (
    [FromQuery] string? pageToken,      // Непрозрачный токен продолжения
    [FromQuery] int? maxPageSize,       // Макс. элементов на страницу
    [FromQuery] bool? published,        // Твои параметры фильтрации...
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var response = await sender.Send(
        new Request(pageToken, maxPageSize, published),
        cancellationToken);
    return TypedResults.Ok(response);
});
```

**Параметры:**

- `pageToken` (string?, опционально) — зашифрованный токен из предыдущего ответа, null/пусто для первой страницы
- `maxPageSize` (int?, опционально) — макс. результатов на страницу, по умолчанию настроенное значение (обычно 10-50)
- **Все параметры фильтрации/поиска** — должны быть включены в запрос для валидации хеша

## Структура ответа

```csharp
public record PageResponse<T>(T[] Items, string? NextPageToken);
```

**Правила:**

- `Items` — массив результатов текущей страницы
- `NextPageToken` — null если последняя страница, иначе зашифрованный токен для следующей страницы

## Паттерн реализации

### 1. Настройка обработчика

```csharp
public class RequestHandler : IRequestHandler<Request, PageResponse<PostModel>>
{
    private readonly WebApiDbContext _db;
    private readonly LimitOffsetPaging _paging;

    public RequestHandler(WebApiDbContext db, LimitOffsetPaging paging)
    {
        _db = db;
        _paging = paging;
    }

    public async Task<PageResponse<PostModel>> Handle(Request request, CancellationToken ct)
    {
        // Шаг 1: Валидация maxPageSize
        if (!_paging.TryGetMaxPageSize(request.MaxPageSize, out var maxPageSize))
        {
            throw PaginationExceptions.InvalidMaxPageSize();
        }

        // Шаг 2: Декодирование pageToken и валидация параметров запроса
        if (!_paging.TryGetOffsetAndLimit(
            request.PageToken,
            maxPageSize,
            out var offset,
            out var limit,
            request.Published))  // КРИТИЧНО: передай ВСЕ параметры фильтрации
        {
            throw PaginationExceptions.InvalidPageToken();
        }

        // Шаг 3: Построение запроса с фильтрами
        var query = _db.Posts.AsNoTracking();

        if (request.Published is not null)
        {
            query = query.Where(x => x.Published == request.Published.Value);
        }

        // Шаг 4: Выполнение запроса с пагинацией
        // КРИТИЧНО: OrderBy должен быть по стабильному полю (обычно Id)
        var items = await query
            .OrderBy(p => p.Id)
            .Skip(offset!.Value)
            .Take(limit!.Value)
            .Select(p => new PostModel(...))
            .ToArrayAsync(ct);

        // Шаг 5: Создание токена следующей страницы
        var nextPageToken = _paging.CreateNextPageToken(
            items.Length,
            offset.Value,
            limit.Value,
            request.Published);  // КРИТИЧНО: передай те же параметры фильтрации

        return new PageResponse<PostModel>(items, nextPageToken);
    }
}
```

## Критические правила

### 1. Всегда передавай ВСЕ параметры запроса в хеширование

Система пагинации проверяет, что токены используются с теми же параметрами запроса через хеш.

**Правильно:**

```csharp
_paging.TryGetOffsetAndLimit(
    request.PageToken,
    maxPageSize,
    out var offset,
    out var limit,
    request.Published,
    request.Category,
    request.SearchQuery)

_paging.CreateNextPageToken(
    items.Length,
    offset.Value,
    limit.Value,
    request.Published,
    request.Category,
    request.SearchQuery)
```

**Неправильно:**

```csharp
// Пропущены параметры фильтрации — токен будет невалидным при смене фильтров
_paging.TryGetOffsetAndLimit(request.PageToken, maxPageSize, out var offset, out var limit)
```

**Почему:** Предотвращает повторное использование токена страницы с другими фильтрами, что вернуло бы неправильные результаты.

### 2. Всегда сортируй по стабильному полю

**Правильно:**

```csharp
var items = await query
    .OrderBy(p => p.Id)              // Стабильная, уникальная сортировка
    .Skip(offset.Value)
    .Take(limit.Value)
    .ToArrayAsync(ct);
```

**Неправильно:**

```csharp
// Без сортировки — результаты будут непредсказуемыми
var items = await query
    .Skip(offset.Value)
    .Take(limit.Value)
    .ToArrayAsync(ct);

// Сортировка по неуникальному полю — пагинация будет пропускать/дублировать элементы
var items = await query
    .OrderBy(p => p.Category)
    .Skip(offset.Value)
    .Take(limit.Value)
    .ToArrayAsync(ct);
```

**Почему:** Без стабильной сортировки offset-пагинация возвращает неконсистентные результаты при изменении данных.

### 3. Валидируй в правильном порядке

**Правильно:**

```csharp
// 1. Сначала валидируй maxPageSize
if (!_paging.TryGetMaxPageSize(request.MaxPageSize, out var maxPageSize))
    throw PaginationExceptions.InvalidMaxPageSize();

// 2. Затем валидируй и декодируй pageToken
if (!_paging.TryGetOffsetAndLimit(request.PageToken, maxPageSize, out var offset, out var limit))
    throw PaginationExceptions.InvalidPageToken();
```

**Неправильно:**

```csharp
// Валидация pageToken до maxPageSize
if (!_paging.TryGetOffsetAndLimit(request.PageToken, request.MaxPageSize ?? 10, ...))
```

**Почему:** Валидация maxPageSize должна происходить первой, чтобы обеспечить валидный размер страницы перед декодированием токена.

### 4. Возвращай null NextToken на последней странице

Метод `CreateNextPageToken` автоматически возвращает `null`, когда `count < limit`, указывая на последнюю страницу.

```csharp
var nextPageToken = _paging.CreateNextPageToken(
    items.Length,      // Если меньше limit — возвращает null
    offset.Value,
    limit.Value,
    request.Published);

// nextPageToken будет null если items.Length < limit.Value
```

**Никогда не устанавливай `nextPageToken = null` вручную** — пусть хелпер-метод обрабатывает эту логику.

## Типичные ошибки

### Ошибка 1: Забытые параметры запроса в методах токена

```csharp
// НЕПРАВИЛЬНО — пропущен фильтр published в хеше
if (!_paging.TryGetOffsetAndLimit(request.PageToken, maxPageSize, out var offset, out var limit))
    throw PaginationExceptions.InvalidPageToken();

var query = _db.Posts.Where(x => x.Published == request.Published);

var nextPageToken = _paging.CreateNextPageToken(items.Length, offset.Value, limit.Value);
// Проблема: пользователь может переиспользовать токен страницы 2 с другим фильтром
```

**Исправление:** Всегда передавай все параметры запроса в оба метода.

### Ошибка 2: Сортировка по неуникальному или нестабильному полю

```csharp
// НЕПРАВИЛЬНО — Category не уникален
var items = await query
    .OrderBy(p => p.Category)
    .Skip(offset.Value)
    .Take(limit.Value)
    .ToArrayAsync(ct);
// Проблема: несколько постов с одинаковой категорией будут иметь непредсказуемый порядок
```

**Исправление:** Сортируй по уникальному, стабильному полю (обычно `Id`). Для кастомной сортировки используй составную:

```csharp
// ПРАВИЛЬНО — кастомная сортировка со стабильным тайбрейкером
var items = await query
    .OrderBy(p => p.Category)
    .ThenBy(p => p.Id)           // Стабильный тайбрейкер
    .Skip(offset.Value)
    .Take(limit.Value)
    .ToArrayAsync(ct);
```

### Ошибка 3: Необработанный null PageToken

```csharp
// НЕПРАВИЛЬНО — предполагается, что pageToken всегда присутствует
var offset = DecodeToken(request.PageToken);  // Падает на первой странице
```

**Исправление:** Метод `TryGetOffsetAndLimit` обрабатывает null-токены автоматически:

```csharp
// ПРАВИЛЬНО
if (!_paging.TryGetOffsetAndLimit(request.PageToken, maxPageSize, out var offset, out var limit))
    throw PaginationExceptions.InvalidPageToken();
// Если pageToken null/пуст — offset будет 0 (первая страница)
```

### Ошибка 4: Разный порядок параметров

```csharp
// НЕПРАВИЛЬНО — разный порядок параметров в decode и create
_paging.TryGetOffsetAndLimit(
    request.PageToken, maxPageSize, out var offset, out var limit,
    request.Published, request.Category)

var nextPageToken = _paging.CreateNextPageToken(
    items.Length, offset.Value, limit.Value,
    request.Category, request.Published)  // Перепутан порядок!
// Проблема: хеш будет другим, валидация токена провалится
```

**Исправление:** Сохраняй ТОЧНО ТОТ ЖЕ порядок параметров для обоих вызовов.

## Конфигурация

Поведение пагинации настраивается в `appsettings.json`:

```json
{
  "Paging": {
    "TokenEncryptionKeyInBase64": "...",  // 32-байтный AES-ключ
    "TokenIvInBase64": "...",             // 16-байтный IV
    "DefaultMaxPageSize": 10,             // По умолчанию если не указано
    "MaxMaxPageSize": 100                 // Верхний лимит
  }
}
```

**Правила:**

- Клиент может запросить любой `maxPageSize` до `MaxMaxPageSize`
- Если клиент превышает лимит, запрос отклоняется с ошибкой `InvalidMaxPageSize`
- Если клиент не указал `maxPageSize`, используется `DefaultMaxPageSize`

## Обработка ошибок

```csharp
// Невалидный maxPageSize (отрицательный, ноль или превышает макс.)
throw PaginationExceptions.InvalidMaxPageSize();
// Возвращает: 400 Bad Request с кодом ошибки "paging:validation:max_page_size_invalid"

// Невалидный pageToken (повреждён, истёк, неверные параметры запроса)
throw PaginationExceptions.InvalidPageToken();
// Возвращает: 400 Bad Request с кодом ошибки "paging:validation:page_token_invalid"
```

## Безопасность

**Шифрование токенов:**

- Токены страниц содержат offset и хеш параметров запроса
- Зашифрованы AES-256 с настроенным ключом и IV
- Закодированы Crockford Base32 для URL-безопасности
- Пользователи не могут прочитать или подделать токены

**Валидация запросов:**

- Токен включает SHA-256 хеш всех параметров запроса
- Предотвращает переиспользование токена с другими фильтрами
- При смене фильтров токен отклоняется

## Тестирование пагинации

При тестировании пагинированных эндпоинтов:

```csharp
[Fact]
public async Task Should_paginate_posts()
{
    // Arrange
    var posts = Enumerable.Range(1, 25)
        .Select(i => CreatePost(name: $"Post {i}"))
        .ToArray();
    await _fixture.Database.Save(posts);

    var client = _fixture.CreateClient();

    // Act — первая страница
    var page1 = await client.GetFromJsonAsync<PageResponse<PostModel>>(
        "/posts?maxPageSize=10");

    // Assert — первая страница
    page1.ShouldNotBeNull();
    page1.Items.Should().HaveCount(10);
    page1.NextPageToken.Should().NotBeNullOrEmpty();

    // Act — вторая страница
    var page2 = await client.GetFromJsonAsync<PageResponse<PostModel>>(
        $"/posts?maxPageSize=10&pageToken={page1.NextPageToken}");

    // Assert — вторая страница
    page2.ShouldNotBeNull();
    page2.Items.Should().HaveCount(10);
    page2.NextPageToken.Should().NotBeNullOrEmpty();

    // Act — третья страница (последняя)
    var page3 = await client.GetFromJsonAsync<PageResponse<PostModel>>(
        $"/posts?maxPageSize=10&pageToken={page2.NextPageToken}");

    // Assert — последняя страница
    page3.ShouldNotBeNull();
    page3.Items.Should().HaveCount(5);
    page3.NextPageToken.Should().BeNullOrEmpty();

    // Assert — нет дубликатов между страницами
    var allIds = page1.Items
        .Concat(page2.Items)
        .Concat(page3.Items)
        .Select(p => p.Id)
        .ToArray();
    allIds.Should().OnlyHaveUniqueItems();
}

[Fact]
public async Task Should_reject_token_when_filters_change()
{
    // Arrange
    var client = _fixture.CreateClient();

    var page1 = await client.GetFromJsonAsync<PageResponse<PostModel>>(
        "/posts?published=true&maxPageSize=10");

    // Act — переиспользование токена с другим фильтром
    var response = await client.GetAsync(
        $"/posts?published=false&maxPageSize=10&pageToken={page1.NextPageToken}");

    // Assert — токен отклонён
    response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
}
```

## Чеклист

Перед завершением реализации пагинации:

- [ ] Запрос имеет параметры `pageToken` и `maxPageSize`
- [ ] Ответ использует `PageResponse<T>` с `Items` и `NextPageToken`
- [ ] Обработчик валидирует `maxPageSize` первым, затем `pageToken`
- [ ] ВСЕ параметры запроса/фильтрации переданы в `TryGetOffsetAndLimit`
- [ ] Те же параметры переданы в `CreateNextPageToken` в том же порядке
- [ ] Запрос использует `.OrderBy(x => x.Id)` или стабильную составную сортировку
- [ ] Запрос использует `.Skip(offset.Value).Take(limit.Value)`
- [ ] Тесты проверяют пагинацию через несколько страниц
- [ ] Тесты проверяют отсутствие дубликатов между страницами
- [ ] Тесты проверяют что `nextPageToken` равен null на последней странице
- [ ] Тесты проверяют отклонение токена при смене фильтров

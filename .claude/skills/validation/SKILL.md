---
name: validation
description: Используй при реализации валидации форм или входных данных — предоставляет full-stack паттерны валидации с FluentValidation (backend), Zod (frontend) и обработкой ошибок ProblemDetails (проект)
---

# Full-Stack валидация

Все пользовательские входные данные в DrimAgents должны валидироваться и на фронтенде (UX), и на бэкенде (безопасность/источник истины).

## Когда использовать

Используй этот скилл когда:

- Создаёшь формы, принимающие пользовательский ввод
- Реализуешь эндпоинты, получающие данные
- Добавляешь функциональность загрузки файлов
- Валидируешь query-параметры или параметры маршрутов
- Реализуешь валидацию бизнес-правил

## Поток валидации

```text
Пользователь заполняет форму
  |
Zod валидирует (клиент, мгновенная обратная связь)
  |
Невалидно -> Показать ошибки сразу (без API-вызова)
  |
Валидно -> Отправить в BFF
  |
BFF передаёт на Backend (без валидации)
  |
FluentValidation валидирует (сервер, авторитетный)
  |
Невалидно -> Вернуть ProblemDetails (400)
  |
Валидно -> Выполнить бизнес-логику
  |
Вернуть успешный ответ
```

**Ключевой принцип:** Фронтенд валидирует для UX, бэкенд валидирует для безопасности. Бэкенд всегда является источником истины.

## Соглашение о кодах ошибок

Паттерн: `domain:entity:field:error_type`

**Примеры:**

- `blog:post:title:required`
- `blog:post:slug:invalid_format`
- `blog:post:slug:already_exists`
- `skills:skill:name:too_long`
- `users:email:invalid_format`
- `users:avatar:file_too_large`
- `courses:lesson:video:invalid_mime_type`

**Использование:**

- Бэкенд возвращает коды ошибок в расширениях ProblemDetails
- Фронтенд маппит коды ошибок на ключи i18n
- Fallback на английское сообщение, если нет перевода

## Руководства по реализации

Подробные паттерны и примеры смотри в:

- **[Валидация на бэкенде](backend-validation.md)** — паттерны FluentValidation, асинхронная валидация, загрузка файлов, интеграция с ProblemDetails
- **[Валидация на фронтенде](frontend-validation.md)** — схемы Zod, React Hook Form, обработка ошибок, i18n
- **[Общие паттерны](validation-patterns.md)** — валидация slug, email, URL, пароля, дат на всех уровнях
- **[Тестирование](validation-testing.md)** — unit-тесты валидаторов с FluentValidation.TestHelper

## Краткий справочник

### Бэкенд (FluentValidation)

```csharp
public class RequestValidator : AbstractValidator<Request>
{
    public RequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .WithMessage("Title is required")
            .WithErrorCode("blog:post:title:required");

        RuleFor(x => x.Slug)
            .Matches("^[a-z0-9-]+$")
            .WithErrorCode("blog:post:slug:invalid_format");
    }
}
```

### Фронтенд (Zod)

```typescript
const schema = z.object({
  title: z.string().min(1, 'Title is required'),
  slug: z.string().regex(/^[a-z0-9-]+$/, 'Invalid slug format')
});

const form = useForm({
  resolver: zodResolver(schema)
});
```

### BFF-слой

**Паттерн passthrough** — без валидации в BFF, ProblemDetails передаются без изменений.

```typescript
const response = await fetch(`${process.env.BACKEND_URL}/api/posts`, {
  method: 'POST',
  body: await request.text()
});

return new Response(await response.text(), {
  status: response.status
});
```

## Тестирование правил валидации

**КРИТИЧНО: КАЖДАЯ фича с RequestValidator ОБЯЗАНА иметь unit-тесты валидатора.**

**Если ты создал RequestValidator без unit-тестов — это провал.**

Правила валидации тестируются в изоляции с помощью FluentValidation.TestHelper. **НИКОГДА** не тестируй валидацию через компонентные тесты — создавай отдельные тестовые классы валидаторов.

**Организация файлов:**
- Размещай класс тестов валидатора ВНУТРИ файла компонентных тестов как вложенный класс
- Расположение файла: `DrimAgents.Api.Tests/Features/{Domain}/{FeatureName}Tests.cs`
- Пример: `CreateSkillTests.cs` содержит класс `CreateSkillTests` с вложенным `ValidatorTests`
- Добавь необходимые using-директивы:
  - `using DrimAgents.Api.Features.{Domain};` (для доступа к классам фич)
  - `using FluentValidation.TestHelper;` (для TestValidate() и методов проверки)

**Соглашение об именовании классов:**
- Компонентные тесты: `{FeatureName}Tests`
- Тесты валидатора (вложенные): `ValidatorTests` (вложенные внутри компонентных тестов)

**Смотри [validation-testing.md](validation-testing.md) для полных паттернов:**
- Обзор примеров тестирования всех правил валидации
- Используй `TestValidate()` для синхронных валидаторов
- Используй `TestValidateAsync()` для асинхронных валидаторов (проверки в БД)
- Тестируй обязательные поля, лимиты длины, паттерны формата и бизнес-правила
- Тестируй и случаи ошибок, И успешные случаи

## Чеклист

Перед завершением реализации валидации:

**Бэкенд:**

- [ ] Правила FluentValidation определены в `RequestValidator`
- [ ] Коды ошибок следуют соглашению `domain:entity:field:error_type`
- [ ] Асинхронная валидация использует `MustAsync` для проверок в БД
- [ ] ValidationBehavior зарегистрирован в MediatR pipeline
- [ ] ValidationExceptionHandler конвертирует в ProblemDetails
- [ ] **Изолированные unit-тесты валидатора созданы**

**Фронтенд:**

- [ ] Схемы Zod зеркалят правила FluentValidation бэкенда
- [ ] React Hook Form использует `zodResolver` со схемой Zod
- [ ] Клиентская валидация обеспечивает мгновенную обратную связь
- [ ] Серверные ошибки маппятся из ProblemDetails на поля формы
- [ ] Коды ошибок маппятся на i18n-сообщения (если применимо)

**BFF:**

- [ ] API-маршруты передают запросы без изменений
- [ ] API-маршруты сохраняют статус-коды от бэкенда
- [ ] API-маршруты возвращают ProblemDetails без изменений
- [ ] Нет логики валидации в BFF-слое

# AI Issue Tracker

Issue-трекер уровня MVP с двумя AI-фичами: AI-триаж бэклога и агентный AI-ревьюер pull request'ов. Бэкенд на .NET, веб-приложение и BFF на Next.js.

## Стек

| Слой | Технология | Версия |
|------|-----------|--------|
| Язык бэкенда | C# | 14 |
| Рантайм | .NET | 10 (LTS) |
| Web API | ASP.NET Core | 10 (Minimal APIs) |
| ORM | EF Core | 10 |
| БД | PostgreSQL | 17 |
| Оркестрация | Aspire | 13.2 |
| Фронтенд + BFF | Next.js | 16 (App Router) |
| UI | React | 19 |
| Стилизация | Tailwind CSS | 4 |
| Компоненты | shadcn/ui | — |
| Формы | React Hook Form + Zod | — |
| Клиентский server-state | TanStack Query | 5 |
| Рантайм фронта | Node.js | 22 LTS |
| Пакетный менеджер фронта | pnpm | — |
| AI | Claude API (Anthropic) | модель `claude-opus-4-7` |

Локально установлено: .NET SDK 10.0.300, Node.js 22.21.1.

Перед добавлением NuGet/npm-пакетов проверяй последнюю стабильную версию — стек намеренно держим свежим.

## Структура репозитория

```text
backend/
├── src/
│   ├── AppHost/            # Aspire AppHost — оркестрация (Postgres, Api, web)
│   ├── ServiceDefaults/    # Общие Aspire-настройки (телеметрия, health checks)
│   └── Api/
│       ├── Features/       # Вертикальные слайсы по доменам
│       ├── Common/         # Общая инфраструктура по назначениям (Auth, Http, ...)
│       ├── Domain/         # EF-сущности
│       └── Program.cs
└── tests/
    └── Api.Tests/          # Компонентные тесты (TestContainers)
web/                        # Next.js — UI + BFF (route handlers проксируют в Api)
```

Запуск всего стека: `dotnet run --project backend/src/AppHost`.

## Архитектура бэкенда

Бэкенд построен на **Vertical Slice Architecture**. Каждая фича — один статический класс-файл в `Features/[Domain]/` с вложенными `Endpoint`, `Request`, `Response`, `RequestValidator`, `RequestHandler`. Поток: HTTP → `IEndpoint` → MediatR `ISender` → `RequestHandler`. Валидация (FluentValidation) запускается автоматически через MediatR pipeline behavior.

Используй проектные скиллы — они единственный источник истины по паттернам:

- **vertical-slice-architecture** — структура слайса, MediatR, IEndpoint, Options-паттерн.
- **component-testing** — тесты через HTTP-эндпоинт с реальными зависимостями (TestContainers + harness).
- **id-generation** — IdGen для распределённых, упорядоченных по времени ID. Не используй `Guid.NewGuid()` для новых сущностей.
- **token-pagination** — AIP-158 токенная пагинация для list-эндпоинтов (фильтр Issues).
- **error-handling** — ProblemDetails, доменные исключения, HTTP-коды full-stack.
- **validation** — FluentValidation (backend) + Zod (frontend).
- **spec-maintenance** — обновление спецификаций модулей после изменений.
- **design-brainstorming** — ОБЯЗАТЕЛЕН перед любой новой фичей или изменением поведения.
- **implementation-planning** — поэтапный план после финализации дизайна.

При работе над AI-фичами используется скилл **claude-api** (промпт-кеширование, tool use).

## Архитектура фронтенда и BFF

`web/` — единое Next.js-приложение, совмещающее UI и **BFF** (Backend-for-Frontend). Браузер общается только с BFF; .NET API наружу не публикуется и доступен лишь из Next.js внутри сети Aspire.

### Граница безопасности

BFF — единственная точка, проверяющая сессию. Поток аутентификации:

1. Логин/регистрация — POST в route handler BFF (`app/api/auth/...`).
2. BFF вызывает .NET API для проверки пароля, затем ставит **httpOnly, Secure, SameSite=Lax** сессионную cookie. JWT/токены в браузер не отдаются.
3. На каждый запрос к .NET API BFF читает сессию и форвардит **доверенный заголовок** с идентификатором пользователя (например, `X-User-Id`).
4. .NET API доверяет этому заголовку (`UserContextMiddleware`: заголовок → `Claims`), так как принимает трафик только от BFF.

### Конвенции фронтенда

- **App Router**. Server Components по умолчанию; `'use client'` — только там, где нужны интерактивность, состояние или браузерные API.
- **Чтение данных** — в Server Components, серверным `fetch` к .NET API через хелпер, который подставляет заголовок пользователя. Список Issues с фильтрами/пагинацией на клиенте — через **TanStack Query**.
- **Мутации** — через route handlers BFF (`app/api/...`), которые проксируют в .NET API. Клиент шлёт `fetch` на свой же `/api/...`.
- **Формы** — React Hook Form + `zodResolver`. Zod-схемы зеркалируют правила FluentValidation бэкенда (детали — в скилле `validation`).
- **Ошибки** — бэкенд возвращает `ProblemDetails`; BFF пробрасывает их как есть, формы раскладывают `errors`/`errorCodes` по полям (скилл `error-handling`).
- **UI** — компоненты shadcn/ui поверх Tailwind CSS; общие компоненты в `web/components/ui`.
- **Дизайн-система — обязательна.** Любой UI (экраны, компоненты, бейджи статусов/приоритетов) строится только из токенов и паттернов [`DESIGN-SYSTEM.md`](DESIGN-SYSTEM.md). Не вводи произвольные цвета, отступы, радиусы или размеры — при нехватке токена сначала дополни дизайн-систему, потом используй.
- **TypeScript strict**, без `any`. Типы API-контрактов держим в `web/lib/api`.
- **Тесты** — Jest + React Testing Library для компонентов и форм.

### Структура web/

```text
web/
├── app/
│   ├── (auth)/             # Логин, регистрация
│   ├── projects/           # Проекты, Issues, доска
│   └── api/                # BFF: route handlers (auth, проксирование в .NET API)
├── components/
│   ├── ui/                 # Примитивы shadcn/ui
│   └── ...                 # Доменные компоненты
├── lib/
│   ├── api/                # Серверный клиент .NET API + типы контрактов
│   ├── auth/               # Сессия, чтение/запись cookie
│   └── validations/        # Zod-схемы
└── middleware.ts           # Защита маршрутов: редирект неаутентифицированных
```

## Домен

Сущности и связи:

```text
User    1—N  Project (owner)
User    1—N  Issue   (reporter и assignee — две отдельные связи)
User    1—N  Comment (author)
Project 1—N  Issue
Project 1—N  Label
Issue   M—N  Label
Issue   1—N  Comment
```

- **User** — `email` (уникален, логин), `password_hash`, `name`, `avatar?`. Аутентификация логин/пароль, без OAuth.
- **Project** — `slug`, `name`, `description?`, `owner`. Свой набор labels.
- **Issue** — `title` (≤200), `description?` (markdown), `status`, `priority`, `assignee?`, `reporter`, `labels[]`, `acceptance_criteria?` (markdown, флаг `ai_suggested`), `created_at`, `updated_at`, `closed_at?`.
- **Label** — `name` (уникален в рамках Project, регистронезависимо), `color`.
- **Comment** — `author`, `body` (markdown), `created_at`, `updated_at`. Сортировка по `created_at` asc; править/удалять может только автор.

Enum'ы: `status` = backlog | todo | in-progress | in-review | done; `priority` = low | medium | high | urgent.

### Инварианты

- Issue создаётся в `backlog`, `priority = medium`, без assignee.
- `reporter` = текущий пользователь при создании, не меняется.
- `assignee` — любой пользователь системы (понятия «участник проекта» нет).
- `closed_at` ставится автоматически при переходе в `done`, обнуляется при выходе из `done`.
- Переходы статусов — нестрогие: вперёд, назад и через шаг разрешены.
- `Label.name` уникален в рамках Project без учёта регистра.
- AI-сгенерированный `acceptance_criteria` помечается `ai_suggested: true`.

## AI-фичи

### AI-триаж (простая)
По Issue (title + description) и контексту проекта (name, description, список labels) Claude **предлагает** `priority`, `labels` (только из существующих в проекте — список зашивается в промпт) и черновик `acceptance_criteria`. Пользователь принимает, правит или переписывает. Маленький промпт, дешёвые ошибки — итерируй промпт на 10–20 эталонных Issues.

### AI-ревьюер PR (агентный)
По URL pull request'а агент через кастомные tools (`fetch_pr_metadata`, `fetch_pr_diff`, `fetch_changed_files`, `fetch_file_content`) сам ходит в GitHub за контекстом, читает diff в свете title / description / acceptance_criteria и оставляет ревью комментарием к Issue с явной привязкой к каждому критерию приёмки. Доступен на Issue в статусе `in-review`; ревью можно перезапускать.

API-ключ Claude и GitHub-токен — через Options-паттерн и user-secrets/конфигурацию Aspire, никогда в коде.

## Конвенции

- Commit-сообщения — conventional commits.
- DTO наружу, никогда не возвращай EF-сущности из эндпоинтов.
- Конфигурация — только через Options-паттерн, не инжектируй `IConfiguration` в обработчики.
- Тесты — компонентные через HTTP; правила валидации тестируются отдельно через `FluentValidation.TestHelper`.

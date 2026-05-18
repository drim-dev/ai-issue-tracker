# AI Issue Tracker

Issue-трекер уровня MVP с двумя AI-фичами: AI-триаж бэклога и агентный AI-ревьюер
pull request'ов. Бэкенд на .NET, веб-приложение и BFF на Next.js.

## Стек

| Слой | Технология |
|------|-----------|
| Бэкенд | C# 14, .NET 10, ASP.NET Core 10 (Minimal APIs), EF Core 10 |
| БД | PostgreSQL 17 |
| Оркестрация | Aspire 13.2 |
| Фронтенд + BFF | Next.js 16 (App Router), React 19 |
| UI | Tailwind CSS 4, shadcn/ui |
| Формы / server-state | React Hook Form + Zod, TanStack Query 5 |
| AI | Claude API (Anthropic), модель `claude-opus-4-7` |

## Структура

```text
backend/
├── src/
│   ├── AppHost/         # Aspire AppHost — оркестрация (Postgres, Api, web)
│   ├── ServiceDefaults/ # Общие Aspire-настройки
│   └── Api/             # .NET API — вертикальные слайсы
└── tests/Api.Tests/     # Компонентные тесты (TestContainers)
web/                     # Next.js — UI + BFF
docs/
├── designs/             # Дизайн-документы
├── plans/               # Планы реализации
└── specs/               # Спецификации модулей
```

## Запуск

Требования: .NET SDK 10, Node.js 22 LTS, pnpm, Docker (для PostgreSQL).

Весь стек поднимается через Aspire AppHost:

```sh
dotnet run --project backend/src/AppHost
```

## Архитектура

- **Бэкенд** — Vertical Slice Architecture: каждая фича — один файл в
  `Features/[Domain]/` с вложенными Endpoint, Request, Response, Validator,
  Handler. Поток: HTTP → `IEndpoint` → MediatR → Handler.
- **Фронтенд + BFF** — единое Next.js-приложение. Браузер общается только с BFF;
  .NET API наружу не публикуется. BFF держит сессию в httpOnly-cookie и форвардит
  доверенный заголовок `X-User-Id` в .NET API.

Подробности — в `CLAUDE.md`, спецификации модулей — в `docs/specs/`.

## AI-фичи

- **AI-триаж** — по Issue и контексту проекта Claude предлагает приоритет, метки
  и черновик критериев приёмки.
- **AI-ревьюер PR** — агент через кастомные tools ходит в GitHub за контекстом PR
  и оставляет ревью с привязкой к критериям приёмки.

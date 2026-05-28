# CodeReview

AI-ревьюер pull request'ов: по URL PR агент Claude в цикле tool-use читает
связанные `docs/{designs,plans,specs}`, разбирает diff и оставляет ревью —
общий summary, inline-комментарии на конкретные строки и вердикт. Запускается
из CLI; ядро — вертикальный слайс, готовый к подключению из web-эндпоинта.

## Модель данных

Модуль не хранит состояние в БД. Все данные — runtime-объекты слайса:

| Тип | Ключевые поля | Примечания |
|-----|--------------|------------|
| `ReviewResult` | `Summary`, `Comments[]`, `Verdict` | Итог ревью, возвращаемый handler'ом. |
| `ReviewVerdict` | `Comment \| RequestChanges \| Approve` | Маппится в GitHub event `COMMENT / REQUEST_CHANGES / APPROVE`. |
| `LineComment` | `Path`, `Line`, `Side`, `Body`, `Category` | `Side` ∈ `Left \| Right`. |
| `ReviewCategory` | `SpecMismatch \| Bug \| Convention \| Tests` | Используется в inline-комментариях. |
| `ReviewContext` | `Pr: PrMetadata`, `ChangedFiles[]`, `UnifiedDiff`, `DocPathsFromPrBody[]` | Передаётся в агент и sinks. |
| `PrMetadata` | `Coordinates`, `Title`, `Body`, `HeadSha`, `BaseSha`, `HeadRef`, `BaseRef`, `Author` | |
| `PrFile` | `Path`, `Status`, `Additions`, `Deletions`, `Patch?` | `Patch` — per-file unified diff. |
| `PrCoordinates` | `Owner`, `Repo`, `Number` | Парсится из URL PR. |

## Точки входа

### CLI

```bash
dotnet run --project backend/src/ReviewCli -- review <pr-url> [--post] [--verbose] [--max-turns N]
```

| Параметр | Назначение |
|----------|-----------|
| `<pr-url>` | URL PR `https://github.com/{owner}/{repo}/pull/{number}`. |
| `--post` | Опубликовать ревью в GitHub. По умолчанию только stdout. |
| `--verbose` | Debug-логирование агентного цикла (turn-by-turn трейс). |
| `--max-turns N` | Override `Claude.MaxTurns` для одного запуска. |

Exit codes: `0` — Approve / Comment, `1` — RequestChanges, `2` — ошибка выполнения.

### MediatR

`ReviewPullRequest.Request(PrUrl, PostToGitHub) → ReviewResult` — единая точка
оркестрации, доступная из CLI сегодня и из web-эндпоинта позже.

## Tools агента

| Имя | Назначение |
|-----|------------|
| `list_docs` | Перечисляет файлы под `docs/{designs,plans,specs}`. |
| `read_doc` | Читает один md-файл под `docs/**`. Пути с `..` и выход за пределы `docs/` отвергаются. |
| `fetch_pr_diff` | Возвращает unified diff PR (уже в `ReviewContext`, без round-trip в GitHub). |
| `fetch_changed_file` | Читает head-sha содержимое файла, только если он есть в списке changed files PR. |

## Ключевые поведения

- **Разбор URL** — `https://github.com/{owner}/{repo}/pull/{number}`, допускаются `http`/`https`, query, fragment, `/files`; невалидный URL → `400 code_review:pr_url:invalid`.
- **Сбор контекста** — handler параллельно тянет metadata, files (`per_page=100` с пагинацией) и unified diff GitHub API.
- **Поиск docs** — основной источник: regex `docs/(designs|plans|specs)/...md` по PR body; fallback handler'а — изменённые `docs/**.md` в самом PR; runtime-fallback агента — tool `list_docs` + match по имени файла к title/changed files.
- **Агентный цикл** — Claude Messages API, `MaxTurns = 20` (override через `--max-turns`); цикл крутится на `stop_reason: "tool_use"`, завершается на `"end_turn"`, любой другой `stop_reason` или превышение `MaxTurns` → `502 code_review:agent:failed`.
- **Prompt caching** — `cache_control: ephemeral` на последнем блоке system-промпта и на последнем элементе `tools`. Initial user message не кешируется.
- **Финальный ответ** — строгий JSON `{ summary, verdict, comments[] }`; допускаются обрамляющие ```json-фенсы. Невалидный JSON или неизвестный `verdict`/`category` → `502 code_review:agent:failed`.
- **Валидация inline-комментариев** — каждый комментарий проверяется через парсер hunks: `(path, line, side)` должен лежать внутри патча соответствующего файла. Невалидные отбрасываются, к `Summary` добавляется строка `_Dropped N inline comment(s) outside the diff: ..._`. GitHub Reviews API иначе отвергает весь запрос целиком.
- **Sinks** — `ConsoleReviewSink` рендерит markdown (`## Summary` → `## Inline Comments` с группировкой по файлу → `## Verdict`) и вызывается всегда; `GitHubReviewSink` публикует через `POST /repos/{o}/{r}/pulls/{n}/reviews` с `event` по вердикту, вызывается только при `PostToGitHub = true`. Sinks дёргаются вызывающим (CLI), handler чистый.
- **Безопасность tools** — `read_doc`/`list_docs` ограничены root'ом `docs/`; `fetch_changed_file` — только файлы из changed-files PR. Неизвестное имя tool'а или исключение в tool'е возвращаются агенту как `{ "error": "..." }` без обрыва цикла.
- **GitHub rate limit** — `403` + `X-RateLimit-Remaining: 0` поднимает `429 code_review:github:rate_limited`. Прочие сетевые сбои — стандартный resilience handler (retry на 429/5xx).

## Конфигурация

| Секция | Поле | Назначение |
|--------|------|-----------|
| `Claude` | `ApiKey`, `Model = "claude-opus-4-7"`, `MaxTurns = 20`, `MaxTokens = 8192` | Доступ к Anthropic Messages API. |
| `GitHub` | `Token`, `UserAgent = "ai-issue-tracker-reviewer"` | PAT с доступом на чтение PR и публикацию ревью. |
| `Workspace` | `DocsRoot = "docs"` | Корень для tools `list_docs`/`read_doc`. |

Секреты — через user-secrets `ReviewCli` или env vars (`Claude__ApiKey`, `GitHub__Token`).

## Обработка ошибок

| Ситуация | HTTP-код | Error code |
|----------|----------|------------|
| Пустой `PrUrl` | `400` | `code_review:pr_url:required` |
| URL не соответствует формату GitHub PR | `400` | `code_review:pr_url:invalid` |
| GitHub rate limit исчерпан | `429` | `code_review:github:rate_limited` |
| Агент превысил `MaxTurns`, вернул неожиданный `stop_reason` или невалидный JSON | `502` | `code_review:agent:failed` |

## Вне зоны

- Публикация ревью обратно в Issue трекера (модуль Issue ещё не реализован — пока контекст ревью берётся только из `docs/`).
- Web-эндпоинт поверх `ReviewPullRequest.Request` (придёт вместе с модулем Issue).
- Octokit и абстракция над LLM-провайдерами — намеренно не используются (теряется prompt caching и качество).
- OAuth GitHub App вместо PAT, webhook-триггер ревью на открытие PR, кеширование ответов агента между запусками.

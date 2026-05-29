# Дизайн: AI-ревьюер pull request'ов

**Дата:** 2026-05-27
**Статус:** провалидирован, готов к планированию реализации
**Ветка:** `feat/ai-pr-reviewer`

## Контекст и цель

Реализовать агентного AI-ревьюера pull request'ов, который читает связанные с задачей `docs/designs|plans|specs` и ревьюит PR с учётом этого контекста. Замечает несоответствия спецификации, баги, нарушения проектных конвенций и пробелы в тестах. Оставляет общий summary и inline-комментарии на конкретные изменённые строки — как при ручном ревью.

Запуск из CLI (раннер), но всё ядро — обычный вертикальный слайс, который позже без переписывания подключится к web-эндпоинту.

В оригинальной задумке (CLAUDE.md) ревьюер был привязан к Issue в статусе `in-review`. Сейчас модуля Issue ещё нет, поэтому контекст ревью берётся из локальных `docs/`.

## Поток

```text
CLI args (PR URL [+ опции])
   │
   ▼
ReviewCli host: DI + Options + ISender.Send(ReviewPullRequest.Request)
   │
   ▼
Features/CodeReview/ReviewPullRequest.RequestHandler
   │  1. GitHub: PR metadata, changed files, unified diff
   │  2. Извлекает пути docs/** из PR body (regex)
   │  3. Fallback: list docs/{designs,plans,specs} + матч по title / имени модуля
   │  4. Запускает агент Claude в tool-use цикле:
   │       list_docs · read_doc · fetch_pr_diff · fetch_changed_file
   │  5. Парсит финальный JSON; валидирует line-комментарии против hunks
   │  6. Возвращает ReviewResult
   │
   ▼
IReviewSink — ConsoleSink всегда; GitHubReviewSink по флагу --post
```

## Решения

- **Источник PR:** GitHub по URL через `IGitHubClient` поверх `HttpClient` (без Octokit — нужно ~5 endpoint'ов).
- **Связь PR ↔ docs:** основной источник — пути в описании PR (regex по `docs/(designs|plans|specs)/...md`); fallback — сопоставление имён доков с атрибутами PR (title, changed files), сам агент через tool `list_docs`.
- **Output:** общий summary + inline-комментарии на конкретные строки через GitHub Reviews API (`POST /pulls/{n}/reviews` с массивом `comments`). Verdict: `COMMENT` / `REQUEST_CHANGES` / `APPROVE`.
- **Line-комментарии:** только на изменённые строки (валидные позиции в diff). Перед публикацией каждый комментарий валидируется против hunks; невалидные отбрасываются с предупреждением в summary.
- **Фокус ревью:** соответствие spec/design/plan, корректность и баги, проектные конвенции (CLAUDE.md, скиллы), покрытие тестами.
- **Модель:** только Claude через прямой Messages API (`claude-opus-4-7`) с prompt caching. Без Claude Agent SDK (нет .NET-версии, ценность мала) и без абстракции над провайдерами (теряем caching и качество).
- **Размещение:** ядро как VSA-слайс в `Api/Features/CodeReview/`. Отдельный проект `ReviewCli` ссылается на `Api` как class library. Web позже добавит endpoint к тому же `ReviewPullRequest.Request`.

## Структура файлов

```text
backend/src/
├── Api/Features/CodeReview/
│   ├── ReviewPullRequest.cs              # Request/Response/Validator/Handler
│   ├── ReviewResult.cs                   # ReviewResult, LineComment, Verdict, Category
│   ├── Agent/
│   │   ├── ICodeReviewAgent.cs
│   │   ├── ClaudeCodeReviewAgent.cs      # tool loop поверх Messages API
│   │   ├── Tools/
│   │   │   ├── ListDocsTool.cs
│   │   │   ├── ReadDocTool.cs
│   │   │   ├── FetchPrDiffTool.cs
│   │   │   └── FetchChangedFileTool.cs
│   │   └── Prompts/SystemPrompt.cs       # под cache_control
│   ├── GitHub/
│   │   ├── IGitHubClient.cs
│   │   ├── GitHubClient.cs
│   │   └── Models.cs                     # PrMetadata, PrFile, DiffHunk
│   ├── Sinks/
│   │   ├── IReviewSink.cs
│   │   ├── ConsoleReviewSink.cs
│   │   └── GitHubReviewSink.cs
│   └── Options/
│       ├── ClaudeOptions.cs
│       └── GitHubOptions.cs
└── ReviewCli/
    ├── Program.cs                        # DI host + System.CommandLine
    └── ReviewCli.csproj                  # → Api
```

## Контракты

```csharp
// MediatR слайс
public sealed record Request(string PrUrl, bool PostToGitHub) : IRequest<ReviewResult>;

public sealed record ReviewResult(
    string Summary,
    IReadOnlyList<LineComment> Comments,
    ReviewVerdict Verdict);                       // Comment | RequestChanges | Approve

public sealed record LineComment(
    string Path, int Line, DiffSide Side,
    string Body, ReviewCategory Category);        // SpecMismatch | Bug | Convention | Tests

public interface ICodeReviewAgent {
    Task<ReviewResult> ReviewAsync(ReviewContext ctx, CancellationToken ct);
}

public sealed record ReviewContext(
    PrMetadata Pr,
    IReadOnlyList<PrFile> ChangedFiles,
    string UnifiedDiff,
    IReadOnlyList<string> DocPathsFromPrBody);

public interface IToolDispatcher {
    ToolDefinition[] Definitions { get; }
    Task<string> ExecuteAsync(string name, JsonElement input, CancellationToken ct);
}
```

## Агент: tool loop и prompt caching

```csharp
var messages = new List<Message> {
    new("user", BuildInitialUserMessage(ctx))
};

for (int turn = 0; turn < options.MaxTurns; turn++) {
    var resp = await anthropic.Messages.CreateAsync(new MessageRequest {
        Model = "claude-opus-4-7",
        MaxTokens = 8192,
        System = SystemPrompt.Blocks,             // cache_control на последнем блоке
        Tools  = dispatcher.Definitions,          // тоже кешируются
        Messages = messages,
    }, ct);

    messages.Add(new("assistant", resp.Content));

    if (resp.StopReason == "end_turn") break;
    if (resp.StopReason != "tool_use")
        throw new ReviewAgentException($"Unexpected stop_reason: {resp.StopReason}");

    var toolResults = new List<ContentBlock>();
    foreach (var use in resp.Content.OfType<ToolUseBlock>()) {
        var output = await dispatcher.ExecuteAsync(use.Name, use.Input, ct);
        toolResults.Add(new ToolResultBlock(use.Id, output));
    }
    messages.Add(new("user", toolResults));
}

return ParseFinalReview(messages.Last());
```

**Prompt caching:**

- `system` — несколько блоков, последний с `cache_control: { type: "ephemeral" }`. Содержимое: роль агента и правила ревью, выдержки из CLAUDE.md (стек, VSA, конвенции), описание категорий замечаний, формат финального ответа.
- `tools` — под `cache_control`. Anthropic кеширует префикс до последнего breakpoint; system + tools переиспользуются между запусками (TTL 5 мин).
- Initial user message **не кешируется** — PR-специфичный контент.

**Формат финального ответа:**

```json
{
  "summary": "...",
  "verdict": "comment|request_changes|approve",
  "comments": [
    { "path": "...", "line": 42, "side": "RIGHT",
      "category": "spec_mismatch|bug|convention|tests",
      "body": "..." }
  ]
}
```

**Защита от циклов:** `MaxTurns = 20` (через `ClaudeOptions`). Превышение → `ReviewAgentException`.

**Ошибки tools:** dispatcher возвращает `tool_result` с `is_error: true`, не бросает. Агент сам решает, как поступить.

## GitHub-интеграция

`HttpClient` через `IHttpClientFactory`, base `https://api.github.com`, заголовки `Authorization: Bearer <PAT>`, `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`. Retry на 429/5xx через `AddStandardResilienceHandler`.

**Парсинг URL:** `https://github.com/{owner}/{repo}/pull/{number}` → `PrCoordinates`. Невалидный URL → `InvalidPrUrlException` (маппится в ProblemDetails).

**Чтение PR:**

| Цель | Endpoint |
|---|---|
| Метаданные | `GET /repos/{o}/{r}/pulls/{n}` |
| Список файлов + per-file patch | `GET /repos/{o}/{r}/pulls/{n}/files?per_page=100` (пагинация) |
| Unified diff | `GET /pulls/{n}` с `Accept: application/vnd.github.v3.diff` |
| Полный head-файл | `GET /repos/{o}/{r}/contents/{path}?ref={head_sha}` |

**Валидация line-комментариев против diff:** парсер hunks (scan по `@@ -a,b +c,d @@`), проверка вхождения `(path, line, side)`. GitHub Reviews API отбрасывает весь запрос, если хотя бы один inline вне diff — поэтому фильтрация обязательна.

**Публикация:**

```http
POST /repos/{o}/{r}/pulls/{n}/reviews
{
  "commit_id": "<head_sha>",
  "body": "<summary>",
  "event": "COMMENT" | "REQUEST_CHANGES" | "APPROVE",
  "comments": [{ "path": "...", "line": 42, "side": "RIGHT", "body": "..." }]
}
```

## Конфигурация (Options)

```csharp
public sealed class GitHubOptions {
    public string Token { get; init; } = "";
    public string UserAgent { get; init; } = "ai-issue-tracker-reviewer";
}

public sealed class ClaudeOptions {
    public string ApiKey { get; init; } = "";
    public string Model { get; init; } = "claude-opus-4-7";
    public int MaxTurns { get; init; } = 20;
}
```

Регистрация: `AddOptions<T>().BindConfiguration("...").ValidateDataAnnotations().ValidateOnStart()`. Секреты — user-secrets на `ReviewCli` либо env vars (`Claude__ApiKey`, `GitHub__Token`).

## CLI-раннер

```bash
dotnet run --project backend/src/ReviewCli -- \
  review <pr-url> [--post] [--verbose] [--max-turns 20]
```

| Опция | Назначение |
|---|---|
| `<pr-url>` | URL PR (positional) |
| `--post` | Опубликовать ревью в PR (по умолчанию только stdout) |
| `--verbose` | Логировать turn-by-turn агент-трейс |
| `--max-turns` | Override `ClaudeOptions.MaxTurns` |

`Host.CreateApplicationBuilder` + `System.CommandLine`. Ссылается на `Api` как class library (без веб-хоста). `ConsoleRenderer` группирует inline-комментарии по файлу.

**Exit codes:** `0` — Approve/Comment, `1` — RequestChanges, `2` — ошибка выполнения.

## Тестирование

**1. Компонентный тест слайса** (`Api.Tests/Features/CodeReview/`) через harness, две внешние зависимости подменяются:

- `IGitHubClient` → fake, читает фикстуры из `Fixtures/<scenario>/{metadata.json, files.json, diff.txt}`.
- `IAnthropicClient` → fake, проигрывает заранее записанный JSON-плейбук tool loop'а.

Кейсы: happy path, spec mismatch, невалидный line-комментарий (drop), MaxTurns exceeded.

**2. Юнит-тесты:**

- `DiffParser` — парсинг hunk-заголовков, проверка позиции.
- `PrUrlParser` — валидные/невалидные URL.
- `DocPathExtractor` — regex + fallback-матч по имени модуля.
- `ToolDispatcher` — unknown tool → `is_error: true`, не исключение.

**3. Интеграционный smoke** одного теста GitHub-клиента на публичный PR, `[Trait("Category","Integration")]`, `Skip.IfNoToken()`.

**Не тестируем:** качество промпта и реальные ответы Claude (эволюционируем на ~10 эталонных PR), реальную публикацию ревью в GitHub (проверяем глазами на dogfooding PR).

**Структура фикстур:**

```text
Api.Tests/Features/CodeReview/Fixtures/
├── pr-auth-happy/
│   ├── metadata.json
│   ├── files.json
│   ├── diff.txt
│   └── agent-playbook.json
└── pr-spec-mismatch/...
```

## Открытые вопросы / следующие шаги

- Промпт-инжиниринг: на этапе реализации собрать 5–10 эталонных PR (включая исторические из этого репо) и итерировать system prompt.
- Подключение к web-приложению — отдельный дизайн, когда появится модуль Issue (агент станет привязан к Issue в `in-review`, как изначально задумано в CLAUDE.md).

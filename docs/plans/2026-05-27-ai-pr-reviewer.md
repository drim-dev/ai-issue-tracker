# План реализации: AI-ревьюер pull request'ов

Дизайн: `docs/designs/2026-05-27-ai-pr-reviewer.md`

Контракты и решения — в дизайне. Здесь только разбивка работы. Backend-задачи следуют скиллу `vertical-slice-architecture`; тесты — `component-testing`; ошибки — `error-handling`.

## Фаза 1 — Каркас, контракты, чистые утилиты

*Без зависимостей. Все члены команды работают параллельно.*

### Задача 1.1: Доменные контракты слайса и Options

- [x] Создать `backend/src/Api/Features/CodeReview/ReviewResult.cs`: записи `ReviewResult`, `LineComment` и enum'ы `ReviewVerdict` (Comment | RequestChanges | Approve), `ReviewCategory` (SpecMismatch | Bug | Convention | Tests), `DiffSide` (Left | Right).
- [x] Создать `ReviewContext.cs` (запись `ReviewContext` с `PrMetadata`, `IReadOnlyList<PrFile>`, `UnifiedDiff`, `IReadOnlyList<string> DocPathsFromPrBody`).
- [x] Создать `Options/ClaudeOptions.cs` и `Options/GitHubOptions.cs` с полями из дизайна, data annotations для обязательных.
- [x] Создать `Exceptions.cs` с `InvalidPrUrlException`, `ReviewAgentException`, маппинг в ProblemDetails по скиллу `error-handling`.
- [x] Объявить `ReviewPullRequest.Request` (запись `(string PrUrl, bool PostToGitHub) : IRequest<ReviewResult>`) и `RequestValidator` в `ReviewPullRequest.cs` (handler оставить пустым stub — реализация в Фазе 3).
- [x] Создать пустой extension-метод `ServiceCollectionExtensions.AddCodeReviewFeature` (Options + MediatR, без регистрации компонентов — их добавят задачи фаз 2–3 через свои extension-методы).

**Файлы:** `backend/src/Api/Features/CodeReview/ReviewResult.cs`, `ReviewContext.cs`, `ReviewPullRequest.cs`, `Exceptions.cs`, `Options/ClaudeOptions.cs`, `Options/GitHubOptions.cs`, `ServiceCollectionExtensions.cs`.

### Задача 1.2: GitHub-модели и интерфейсы

- [x] Создать `GitHub/Models.cs`: `PrCoordinates(Owner, Repo, Number)`, `PrMetadata`, `PrFile`, `DiffHunk` — поля по дизайну.
- [x] Создать `GitHub/IGitHubClient.cs` с методами `GetPullRequestAsync`, `GetFilesAsync`, `GetUnifiedDiffAsync`, `GetFileContentAsync`, `PostReviewAsync`.
- [x] Создать `Sinks/IReviewSink.cs` (`Task PublishAsync(ReviewResult, ReviewContext, CancellationToken)`).
- [x] Создать `Agent/ICodeReviewAgent.cs` и `Agent/IToolDispatcher.cs` (методы по дизайну).

**Файлы:** `backend/src/Api/Features/CodeReview/GitHub/Models.cs`, `GitHub/IGitHubClient.cs`, `Sinks/IReviewSink.cs`, `Agent/ICodeReviewAgent.cs`, `Agent/IToolDispatcher.cs`.

### Задача 1.3: Чистые утилиты с юнит-тестами

- [x] `GitHub/PrUrlParser.cs` — статический метод `TryParse`/`Parse` для `https://github.com/{o}/{r}/pull/{n}`; невалидный → `InvalidPrUrlException`.
- [x] `GitHub/DiffParser.cs` — парсинг hunk-заголовков `@@ -a,b +c,d @@`, метод `IsLineInDiff(string patch, int line, DiffSide side)`.
- [x] `Agent/DocPathExtractor.cs` — regex по PR body `docs/(designs|plans|specs)/[\w\-./]+\.md`; fallback-метод `MatchByName(IEnumerable<string> docPaths, string prTitle, IEnumerable<string> changedFiles)`.
- [x] Юнит-тесты для всех трёх: `backend/tests/Api.Tests/Features/CodeReview/{PrUrlParserTests, DiffParserTests, DocPathExtractorTests}.cs`. Табличные xUnit `[Theory]`.

**Файлы:** `backend/src/Api/Features/CodeReview/GitHub/PrUrlParser.cs`, `GitHub/DiffParser.cs`, `Agent/DocPathExtractor.cs`, `backend/tests/Api.Tests/Features/CodeReview/PrUrlParserTests.cs`, `DiffParserTests.cs`, `DocPathExtractorTests.cs`.

### Задача 1.4: Скелет проекта ReviewCli

- [x] Создать `backend/src/ReviewCli/ReviewCli.csproj`: `Exe`, `net10.0`, ссылка на `Api`, пакеты `Microsoft.Extensions.Hosting`, `System.CommandLine` (последняя стабильная).
- [x] Добавить проект в `backend/ai-issue-tracker.sln` (или текущий sln-файл).
- [x] Создать `backend/src/ReviewCli/Program.cs` со скелетом: `Host.CreateApplicationBuilder`, user-secrets, заглушка root-команды `review` без логики (просто печатает «not implemented» — реальная логика в Задаче 3.2).
- [x] Инициализировать user-secrets: `dotnet user-secrets init --project backend/src/ReviewCli`.
- [x] Проверить, что `dotnet build` проходит.

**Файлы:** `backend/src/ReviewCli/ReviewCli.csproj`, `backend/src/ReviewCli/Program.cs`, `backend/ai-issue-tracker.sln` (или эквивалент).

## Фаза 2 — Компоненты

*Зависит от: Фазы 1 (интерфейсы, модели, Options, утилиты).*

### Задача 2.1: GitHubClient

- [x] Реализовать `GitHub/GitHubClient.cs` через `IHttpClientFactory`: base `https://api.github.com`, заголовки `Authorization: Bearer`, `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`.
- [x] Методы по дизайну: `GetPullRequestAsync`, `GetFilesAsync` (с пагинацией `per_page=100`), `GetUnifiedDiffAsync` (через `Accept: application/vnd.github.v3.diff`), `GetFileContentAsync` (через `/contents/{path}?ref=`), `PostReviewAsync`.
- [x] Создать extension `AddGitHubClient(IServiceCollection)` с `AddHttpClient<IGitHubClient, GitHubClient>().AddStandardResilienceHandler()`.
- [x] Доменное исключение `GitHubRateLimitException` при 403 + `X-RateLimit-Remaining: 0`.

**Файлы:** `backend/src/Api/Features/CodeReview/GitHub/GitHubClient.cs`, `GitHub/GitHubServiceCollectionExtensions.cs`.

### Задача 2.2: Tools и ToolDispatcher

- [x] Реализовать 4 tools в `Agent/Tools/`: `ListDocsTool`, `ReadDocTool` (читают локальные `docs/**`), `FetchPrDiffTool`, `FetchChangedFileTool` (через `IGitHubClient`).
- [x] Каждый tool экспонирует `ToolDefinition` (имя, описание, JSON-schema input) и метод `ExecuteAsync(JsonElement input, ReviewContext ctx, CancellationToken)`.
- [x] `ToolDispatcher` в `Agent/ToolDispatcher.cs`: собирает все `ITool` через DI, единый switch по name; unknown name → возвращает `tool_result` с `is_error: true`, не бросает.
- [x] Безопасность `read_doc` и `list_docs`: ограничить root'ом `docs/`, отвергать пути с `..`.
- [x] Extension `AddCodeReviewTools(IServiceCollection)`.

**Файлы:** `backend/src/Api/Features/CodeReview/Agent/Tools/ListDocsTool.cs`, `Tools/ReadDocTool.cs`, `Tools/FetchPrDiffTool.cs`, `Tools/FetchChangedFileTool.cs`, `Agent/ToolDispatcher.cs`, `Agent/ToolsServiceCollectionExtensions.cs`.

### Задача 2.3: ClaudeCodeReviewAgent

- [x] Подключить пакет `Anthropic.SDK` (или прямой HttpClient — на выбор реализатора, ориентируйся на скилл `claude-api`).
- [x] Реализовать `Agent/ClaudeCodeReviewAgent.cs` с tool loop по дизайну: `MaxTurns`, `cache_control: ephemeral` на последнем блоке system и на `tools`, парсинг финального JSON, валидация против `ReviewResult` контракта.
- [x] Создать `Agent/Prompts/SystemPrompt.cs` со статичным контентом: роль, выдержки из CLAUDE.md (стек, VSA, конвенции), категории замечаний, формат JSON-ответа.
- [x] Превышение `MaxTurns` или некорректный `stop_reason` → `ReviewAgentException`.
- [x] Extension `AddClaudeReviewAgent(IServiceCollection)`.
- [x] Применить паттерны промпт-кеширования из скилла `claude-api`.

**Файлы:** `backend/src/Api/Features/CodeReview/Agent/ClaudeCodeReviewAgent.cs`, `Agent/Prompts/SystemPrompt.cs`, `Agent/AgentServiceCollectionExtensions.cs`.

### Задача 2.4: Sinks

- [x] `Sinks/ConsoleReviewSink.cs`: рендерит `## Summary`, `## Inline Comments` (группировка по файлу), `## Verdict` в stdout через `ILogger` или `Console`.
- [x] `Sinks/GitHubReviewSink.cs`: вызывает `IGitHubClient.PostReviewAsync` с маппингом `ReviewVerdict` → `event` (COMMENT/REQUEST_CHANGES/APPROVE).
- [x] Extension `AddReviewSinks(IServiceCollection)`.

**Файлы:** `backend/src/Api/Features/CodeReview/Sinks/ConsoleReviewSink.cs`, `Sinks/GitHubReviewSink.cs`, `Sinks/SinksServiceCollectionExtensions.cs`.

## Фаза 3 — Оркестрация и CLI

*Зависит от: Фазы 2 (все компоненты должны быть зарегистрированы и работоспособны).*

### Задача 3.1: Handler слайса `ReviewPullRequest`

- [x] Реализовать `RequestHandler` в `ReviewPullRequest.cs`: парсит URL → читает PR через `IGitHubClient` → извлекает doc-пути через `DocPathExtractor` → строит `ReviewContext` → вызывает `ICodeReviewAgent.ReviewAsync` → валидирует line-комментарии через `DiffParser` (отбрасывает невалидные, добавляет warning в `Summary`) → возвращает `ReviewResult`.
- [x] Если `Request.PostToGitHub` — после возврата handler'а в `Endpoint`/CLI вызвать `GitHubReviewSink`; `ConsoleReviewSink` вызывается всегда (обе sink-инжекции — в CLI Program.cs, handler чистый).
- [x] Обновить `AddCodeReviewFeature` (из Задачи 1.1): вызвать `AddGitHubClient`, `AddCodeReviewTools`, `AddClaudeReviewAgent`, `AddReviewSinks`. Это единственное место со сборкой DI слайса.

**Файлы:** `backend/src/Api/Features/CodeReview/ReviewPullRequest.cs`, `ServiceCollectionExtensions.cs`.

### Задача 3.2: CLI-команда review

- [x] В `backend/src/ReviewCli/Program.cs`: подключить `AddCodeReviewFeature`, настроить `System.CommandLine` с командой `review <pr-url>`, опциями `--post`, `--verbose`, `--max-turns`.
- [x] Handler команды: получает `ISender`, шлёт `ReviewPullRequest.Request`, печатает результат через `ConsoleReviewSink`, по флагу `--post` — `GitHubReviewSink`. Exit codes: 0/1/2 по дизайну.
- [x] Реализовать `ReviewCli/ConsoleRenderer.cs` для группировки inline-комментариев по файлам (если не покрыто `ConsoleReviewSink` — переиспользовать; иначе тонкая обёртка).
- [x] `--verbose` включает уровень `Debug` для категории `ClaudeCodeReviewAgent` (turn-by-turn трейс).
- [x] Smoke-тест вручную: `dotnet run --project backend/src/ReviewCli -- review <url>` на тестовом PR.

**Файлы:** `backend/src/ReviewCli/Program.cs`, `backend/src/ReviewCli/ConsoleRenderer.cs` (если нужен).

## Фаза 4 — Компонентный тест слайса

*Зависит от: Фазы 3 (handler собран и работает end-to-end с fakes).*

### Задача 4.1: Компонентный тест ReviewPullRequest

- [x] Создать harness в `backend/tests/Api.Tests/Features/CodeReview/ReviewPullRequestTests.cs` по скиллу `component-testing` с двумя fake-зависимостями: `FakeGitHubClient` читает фикстуры, `FakeAnthropicClient` проигрывает playbook.
- [x] Создать фикстуры в `backend/tests/Api.Tests/Features/CodeReview/Fixtures/pr-happy/{metadata.json, files.json, diff.txt, agent-playbook.json}` и `pr-spec-mismatch/...`.
- [x] Покрыть кейсы: happy path (Verdict=Comment), spec mismatch (Verdict=RequestChanges), невалидный line-комментарий (drop + warning в summary), MaxTurns exceeded (ReviewAgentException → ProblemDetails).
- [x] Не тестировать реальный `POST /reviews` и качество промпта — только контракт слайса.

**Файлы:** `backend/tests/Api.Tests/Features/CodeReview/ReviewPullRequestTests.cs`, `backend/tests/Api.Tests/Features/CodeReview/FakeGitHubClient.cs`, `FakeAnthropicClient.cs`, `Fixtures/pr-happy/*`, `Fixtures/pr-spec-mismatch/*`.

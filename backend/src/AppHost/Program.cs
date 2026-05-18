var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithImage("postgres", "17")
    .WithDataVolume()
    .WithPgAdmin();

var appDb = postgres.AddDatabase("appdb");

var api = builder.AddProject<Projects.Api>("api")
    .WithReference(appDb)
    .WaitFor(appDb);

// Секрет шифрования iron-session cookie. Для локальной разработки — параметр
// со значением по умолчанию (≥32 символов); в публикации задаётся через конфиг.
var sessionSecret = builder.AddParameter(
    "session-secret",
    "local-dev-session-secret-change-me-please-0123456789",
    secret: true);

builder.AddNpmApp("web", "../../../web")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(env: "PORT")
    .WithEnvironment("API_BASE_URL", api.GetEndpoint("http"))
    .WithEnvironment("SESSION_SECRET", sessionSecret)
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();

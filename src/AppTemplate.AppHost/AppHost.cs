using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// SQLite file lives next to the AppHost project; *.db is gitignored.
var db = builder.AddSqlite("appdb", builder.AppHostDirectory, "app.db");

// Generated once, persisted to the AppHost user secrets. >= 48 chars, Api needs >= 32 UTF-8 bytes.
var signingKey = builder.AddParameter(
    "preview-signing-key",
    new GenerateParameterDefault { MinLength = 48, Special = false },
    secret: true,
    persist: true);

var api = builder.AddDotnetProject("api", "../AppTemplate.Api/AppTemplate.Api.csproj")
    .WithReference(db)
    .WaitFor(db)
    .WithEnvironment("Preview__SigningKey", signingKey);

builder.AddViteApp("web", "../web")
    .WithNpm(install: true, installCommand: "ci")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();

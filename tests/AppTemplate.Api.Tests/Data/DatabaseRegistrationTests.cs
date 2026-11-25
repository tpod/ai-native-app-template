using AppTemplate.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.Tests.Data;

/// <summary>
/// The app's own database registration, without <see cref="Infrastructure.ApiFactory"/>'s SQLite swap: Postgres only,
/// and a clear failure when no connection string is configured.
/// </summary>
public sealed class DatabaseRegistrationTests
{
    [Fact]
    public async Task AppDbContext_NoConnectionString_FailsWithAppHostHint()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("Preview:SigningKey", Infrastructure.ApiFactory.SigningKey);
        });
        using var scope = factory.Services.CreateScope();

        var ex = Should.Throw<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<AppDbContext>().Model);

        ex.Message.ShouldContain("ConnectionStrings:appdb");
        ex.Message.ShouldContain("src/AppTemplate.AppHost");
    }

    [Fact]
    public void DevelopmentSettings_HaveNoConnectionString()
    {
        var path = Path.Combine(RepoRoot(), "src", "AppTemplate.Api", "appsettings.Development.json");
        File.Exists(path).ShouldBeTrue(path);

        var config = new ConfigurationBuilder().AddJsonFile(path).Build();

        config.GetConnectionString("appdb").ShouldBeNull("The AppHost supplies appdb; a file value would be a stale SQLite string or a guess.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AppTemplate.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("AppTemplate.slnx not found above the test output directory.");
    }
}

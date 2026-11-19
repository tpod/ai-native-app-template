using AppTemplate.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AppTemplate.Api.Tests.Http;

public sealed class ApiTestBaseTests : ApiTestBase
{
    private sealed record Marker(string Value);

    protected override void ConfigureServices(IServiceCollection services)
        => services.AddSingleton(new Marker("from-subclass"));

    [Fact]
    public void Services_SubclassRegisteredService_IsResolvable()
        => Factory.Services.GetRequiredService<Marker>().Value.ShouldBe("from-subclass");
}

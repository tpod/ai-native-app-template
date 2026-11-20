using AppTemplate.Api.Actions;
using AppTemplate.Api.Actions.Http;
using AppTemplate.Api.Data;
using AppTemplate.Api.Mcp;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("appdb")));

builder.Services.AddAppProblemDetails();
builder.Services.AddActions();
builder.Services.AddAppMcp();
builder.Services.AddAppOpenApi();

var app = builder.Build();

app.UseAppProblemDetails();

app.UseDefaultFiles();
app.UseStaticFiles();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.MapDefaultEndpoints();
app.MapActions();
app.MapAppOpenApi();
app.MapAppMcp("/mcp");

app.MapFallback("/api/{**path}", (HttpContext ctx) => AppErrorProblemDetails.ToResult(new RequestNotFound(ctx.Request.Path.Value ?? "/"), ctx));
app.MapFallback("/mcp/{**path}", (HttpContext ctx) => AppErrorProblemDetails.ToResult(new RequestNotFound(ctx.Request.Path.Value ?? "/"), ctx));

// Registered last so it only catches paths the /api and /mcp fallbacks above do not claim.
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;

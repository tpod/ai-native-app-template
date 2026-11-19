using AppTemplate.Api.Actions;
using AppTemplate.Api.Actions.Http;
using AppTemplate.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(builder.Configuration.GetConnectionString("appdb")));

builder.Services.AddAppProblemDetails();

var app = builder.Build();

app.UseAppProblemDetails();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.Migrate();
}

app.MapDefaultEndpoints();

app.MapFallback("/api/{**path}", (HttpContext ctx) => AppErrorProblemDetails.ToResult(new RequestNotFound(ctx.Request.Path.Value ?? "/"), ctx));
app.MapFallback("/mcp/{**path}", (HttpContext ctx) => AppErrorProblemDetails.ToResult(new RequestNotFound(ctx.Request.Path.Value ?? "/"), ctx));

app.Run();

public partial class Program;

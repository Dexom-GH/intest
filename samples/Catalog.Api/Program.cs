using Catalog.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// File-backed SQLite: a real relational provider, so unique indexes and restricted foreign
// keys produce genuine 409s. The EF Core InMemory provider enforces neither.
var databasePath = Path.Combine(AppContext.BaseDirectory, "catalog.db");
builder.Services.AddDbContext<CatalogDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));

builder.Services.AddControllers();

// The built-in producer. Note it emits no operationId for controller actions unless
// [EndpointName] is applied — deliberately left off, so InTest's synthesis path is exercised.
builder.Services.AddOpenApi();

var app = builder.Build();

app.MapOpenApi();
app.MapControllers();

app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }))
   .ExcludeFromDescription();

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    await CatalogDbContext.SeedAsync(database);
}

await app.RunAsync();

/// <summary>Exposed so the build-time OpenAPI document generator can locate the entry point.</summary>
public partial class Program;

using Inventory.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var databasePath = Path.Combine(AppContext.BaseDirectory, "inventory.db");
builder.Services.AddDbContext<InventoryDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));

builder.Services.AddControllers();

// NSwag. Unlike the other two producers it always emits an operationId, derived as
// {Controller}_{Action} — stable-looking, but it churns when an action is renamed.
builder.Services.AddOpenApiDocument(settings =>
{
    settings.Title = "Inventory API";
    settings.Version = "v1";
    settings.DocumentName = "v1";
});

var app = builder.Build();

app.MapControllers();
app.UseOpenApi();

app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
    await InventoryDbContext.SeedAsync(database);
}

await app.RunAsync();

/// <summary>Exposed so the NSwag build-time generator can locate the entry point.</summary>
public partial class Program;

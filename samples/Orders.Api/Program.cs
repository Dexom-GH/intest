using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using Orders.Api.Data;

var builder = WebApplication.CreateBuilder(args);

var databasePath = Path.Combine(AppContext.BaseDirectory, "orders.db");
builder.Services.AddDbContext<OrdersDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));

var authority = builder.Configuration["Identity:Authority"] ?? "https://localhost:5443";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
       .AddJwtBearer(options =>
       {
           options.Authority = authority;
           options.Audience = "orders-api";
           options.RequireHttpsMetadata = builder.Environment.IsProduction();
       });

builder.Services.AddAuthorizationBuilder()
       .AddPolicy(Policies.Read, policy => policy.RequireAuthenticatedUser().RequireClaim("scope", "orders.read"))
       .AddPolicy(Policies.Write, policy => policy.RequireAuthenticatedUser().RequireClaim("scope", "orders.write"));

builder.Services.AddControllers();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Orders API", Version = "v1" });

    var xml = Path.Combine(AppContext.BaseDirectory, "Orders.Api.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);

    // Declaring the scheme is what puts `security` in the document, which is what makes
    // InTest generate auth contract tests at all.
    // Explicit concrete types: Microsoft.OpenApi 3.x made several formerly concrete types
    // interfaces, so target-typed `new()` binds to IOpenApiSecurityScheme and will not compile.
    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.OAuth2,
        Flows = new OpenApiOAuthFlows
        {
            ClientCredentials = new OpenApiOAuthFlow
            {
                TokenUrl = new Uri($"{authority}/connect/token"),
                Scopes = new Dictionary<string, string>
                {
                    ["orders.read"] = "Read orders",
                    ["orders.write"] = "Create and modify orders"
                }
            }
        }
    });

    // Per-operation rather than document-wide — see AuthorizeOperationFilter.
    options.DocumentFilter<Orders.Api.OpenApi.AuthorizeOperationFilter>();
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.UseSwagger();

app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }))
   .ExcludeFromDescription()
   .AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
    await OrdersDbContext.SeedAsync(database);
}

await app.RunAsync();

/// <summary>Exposed so the build-time OpenAPI document generator can locate the entry point.</summary>
public partial class Program;

public static class Policies
{
    public const string Read = "orders.read";
    public const string Write = "orders.write";
}

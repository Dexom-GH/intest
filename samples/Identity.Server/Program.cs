using Identity.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIdentityServer(options =>
       {
           // Deterministic issuer: the Orders API validates against this exact value, and a
           // fixture that changed its issuer per run would be untestable.
           options.IssuerUri = builder.Configuration["IdentityServer:IssuerUri"] ?? "https://localhost:5443";
           options.KeyManagement.Enabled = false;
       })
       .AddDeveloperSigningCredential()
       .AddInMemoryApiScopes(Config.ApiScopes)
       .AddInMemoryApiResources(Config.ApiResources)
       .AddInMemoryClients(Config.Clients);

var app = builder.Build();

app.UseIdentityServer();

app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

await app.RunAsync();

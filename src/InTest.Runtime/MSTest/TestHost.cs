using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InTest.Runtime;

/// <summary>
/// Assembly-scope composition root. MSTest-specific by design: it is the adapter, not the
/// neutral layer. Generated projects delegate their [AssemblyInitialize] here.
/// </summary>
public static class TestHost
{
    public static IConfiguration Configuration { get; private set; } = null!;
    public static IServiceProvider Root { get; private set; } = null!;
    public static SchemaBundle Schemas { get; private set; } = null!;
    public static string RunIdValue { get; private set; } = null!;
    public static string Profile { get; private set; } = null!;

    /// <summary>Registration hook. The generated project's TestStartup assigns this before
    /// InitializeAsync runs, so team registrations compose with InTest's.</summary>
    public static Action<IServiceCollection, IConfiguration>? ConfigureServices { get; set; }

    public static async Task InitializeAsync(TestContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        Profile = ResolveProfile(context);
        Configuration = BuildConfiguration(Profile);
        RunIdValue = RunId.Create(Configuration["InTest:RunId:Prefix"]);
        context.WriteLine($"InTest run id: {RunIdValue} (profile '{Profile}')");

        var services = new ServiceCollection();
        services.AddSingleton(Configuration);
        services.AddTransient(_ => new RunIdHandler(() => RunIdValue));
        services.AddHttpClient(InTestClients.Api, client =>
                {
                    client.BaseAddress = InTestUrl.NormalizeBase(
                        Configuration["Api:BaseUrl"]
                        ?? throw new InvalidOperationException(
                            $"Api:BaseUrl is not configured for profile '{Profile}'."));
                })
                .AddHttpMessageHandler<RunIdHandler>();

        ConfigureServices?.Invoke(services, Configuration);
        Root = services.BuildServiceProvider();

        Schemas = SchemaBundle.FromFile(Path.Combine(AppContext.BaseDirectory, "spec-schemas.json"));

        var readiness = new ReadinessOptions();
        Configuration.GetSection("InTest:Readiness").Bind(readiness);

        using var scope = Root.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(InTestClients.Api);
        await Readiness.WaitAsync(client, readiness, cancellationToken).ConfigureAwait(false);
    }

    private static string ResolveProfile(TestContext context)
    {
        if (context.Properties.TryGetValue("profile", out var fromRunSettings) && fromRunSettings is string s && s.Length > 0)
            return s;

        return Environment.GetEnvironmentVariable("INTEST_PROFILE")
               ?? BuildConfiguration(profile: null)["InTest:DefaultProfile"]
               ?? "local";
    }

    private static IConfiguration BuildConfiguration(string? profile)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false);

        if (profile is not null) builder.AddJsonFile($"appsettings.{profile}.json", optional: true);

        return builder.AddJsonFile("appsettings.local.json", optional: true)
                      .AddEnvironmentVariables("INTEST_")
                      .Build();
    }
}

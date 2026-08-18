using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
    public static FixtureStore Fixtures { get; private set; } = null!;

    /// <summary>
    /// One aggregated fixture-validation report, built once at <see cref="InitializeAsync"/> and
    /// consulted by every <c>ApiTestBase.RequireFixture</c> call — never rebuilt per test, and
    /// never bypassed by going straight to <see cref="Fixtures"/> (decision 2 / Task 7).
    /// </summary>
    public static FixtureValidation.Report FixtureValidationReport { get; private set; } = null!;

    /// <summary>
    /// The token resolver built once here and reused by every generated request via
    /// <c>ApiTestBase</c>'s fixture helpers — the same instance <see cref="FixtureValidationReport"/>
    /// was built from, so <c>{{config:}}</c>/<c>{{secret:}}</c> are read once per run (Task 6's
    /// resolution-timing table) while <c>{{utcNow}}</c> still varies per call, because
    /// <c>TokenResolver</c> invokes the clock itself on every <c>Resolve</c> rather than caching it.
    /// </summary>
    public static TokenResolver FixtureTokens { get; private set; } = null!;

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

        Fixtures = FixtureStore.Load(AppContext.BaseDirectory, Profile);
        FixtureTokens = new TokenResolver(Configuration, RunIdValue);
        FixtureValidationReport = FixtureValidation.Build(Fixtures, FixtureTokens);
        // Written once here so every problem across every fixture lands in the .trx and the CI
        // summary, even though only the operations actually blocked go on to fail (decision 2).
        context.WriteLine(FixtureValidationReport.Message);

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

        // Fail on a base URL that repeats a prefix the spec's paths already carry, before a
        // single request is sent. The alternative is every test returning 404 with no clue why.
        InTestUrl.EnsureNoPrefixDuplication(
            InTestUrl.NormalizeBase(Configuration["Api:BaseUrl"]!), ReadOperationPathPrefix());

        var readiness = new ReadinessOptions();
        Configuration.GetSection("InTest:Readiness").Bind(readiness);

        using var scope = Root.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(InTestClients.Api);
        await Readiness.WaitAsync(client, readiness, cancellationToken).ConfigureAwait(false);
    }

    private static string? ReadOperationPathPrefix()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "spec-paths.json");
        if (!File.Exists(path))
        {
            return null;
        }

        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.TryGetProperty("operationPathPrefix", out var value)
            ? value.GetString()
            : null;
    }

    private static string ResolveProfile(TestContext context)
    {
        if (context.Properties.TryGetValue("profile", out var fromRunSettings) && fromRunSettings is string s && s.Length > 0)
        {
            return s;
        }

        return Environment.GetEnvironmentVariable("INTEST_PROFILE")
               ?? BuildConfiguration(profile: null)["InTest:DefaultProfile"]
               ?? "local";
    }

    private static IConfiguration BuildConfiguration(string? profile)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false);

        if (profile is not null)
        {
            builder.AddJsonFile($"appsettings.{profile}.json", optional: true);
        }

        return builder.AddJsonFile("appsettings.local.json", optional: true)
                      .AddEnvironmentVariables("INTEST_")
                      .Build();
    }
}

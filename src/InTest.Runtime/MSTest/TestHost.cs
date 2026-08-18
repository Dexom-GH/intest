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

    /// <summary>
    /// The one <see cref="FixtureContext"/> instance Task 6's <see cref="InitializeAsync"/> will
    /// create and pass to every fixture, retained here so <see cref="CleanupAsync"/> can drain
    /// the exact instance the fixtures wrote to rather than a fresh, empty one (decision 4). Null
    /// until Task 6 populates it, and null again whenever <see cref="InitializeAsync"/> threw
    /// before reaching that point (e.g. a readiness failure). <see cref="CleanupAsync"/> treats
    /// null as "nothing to drain," not an error. Internal because only <see cref="CleanupAsync"/>
    /// reads it and only <see cref="InitializeAsync"/> should write it; a generated project has
    /// no business touching it directly.
    /// </summary>
    internal static FixtureContext? RetainedFixtureContext { get; set; }

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

    /// <summary>
    /// Drains <see cref="RetainedFixtureContext"/> during the generated project's
    /// [AssemblyCleanup] — the caller that makes <see cref="FixtureRunner.DrainAsync"/> (Task 3)
    /// reachable at all, since <see cref="TestHost"/> is a plain static class and cannot carry
    /// the attribute itself.
    /// <para>
    /// Called unconditionally by the scaffolded <c>TestStartup.cs</c>, regardless of whether
    /// <see cref="InitializeAsync"/> succeeded. That is exactly the composition
    /// <see cref="FixtureRunner.DrainAsync"/>'s idempotency exists for: a fixture failure during
    /// <see cref="InitializeAsync"/> already triggers one drain inside
    /// <see cref="FixtureRunner.RunAsync"/> (Task 6 wires that call), so this second,
    /// unconditional drain finds nothing left and is a safe no-op rather than a repeat failure.
    /// </para>
    /// <para>
    /// <see cref="FixtureRunner.DrainAsync"/> throws <see cref="FixtureLifecycleException"/> by
    /// design (Task 3) to report a teardown action that failed. That exception is caught here
    /// rather than rethrown: an exception escaping [AssemblyCleanup] becomes the whole run's
    /// headline, burying whatever test actually failed underneath a teardown complaint — the
    /// drain report is diagnostic, not a verdict. Only <see cref="FixtureLifecycleException"/>
    /// is caught, because that is the only type <see cref="FixtureRunner.DrainAsync"/>'s own
    /// contract promises to throw — a promise <see cref="FixtureRunner.DrainAsync"/> itself
    /// defends even against a misbehaving cause (Task 5's hardening in
    /// <c>FixtureRunnerTests.DrainWrapsACauseEvenWhenItsOwnMessageGetterThrows</c>) — so anything
    /// else escaping from here would be a genuine bug in <see cref="FixtureRunner"/> and must
    /// propagate rather than be swallowed alongside a legitimate teardown failure.
    /// </para>
    /// <para>
    /// Written to both <paramref name="context"/> and <see cref="Console.Error"/>, because
    /// neither sink alone reaches every CI shape: <see cref="TestContext.WriteLine(string)"/>
    /// lands in the .trx but is invisible at <c>dotnet test</c>'s default console verbosity, and
    /// a CI setup that captures console output plus exit code without publishing the .trx would
    /// otherwise never see this failure at all — even though, by design, it does not fail the
    /// run or its exit code.
    /// </para>
    /// <para>
    /// The message names the run id (<see cref="RunIdValue"/>) — the handle an operator has for
    /// finding what a leaked row belongs to, since every request <c>RunIdHandler</c> sends
    /// carries it — falling back to an explicit "unavailable" note when <see cref="RunIdValue"/>
    /// is still its default <c>null!</c> because <see cref="InitializeAsync"/> never reached the
    /// line that assigns it. It names the risk to a <em>later</em> run, not this one: this run's
    /// own results are genuinely unaffected, but that is not the risk worth an operator's
    /// attention — state this run failed to tear down outliving it and breaking the next one is
    /// (§14/F7).
    /// </para>
    /// <para>
    /// <see cref="RetainedFixtureContext"/> is null whenever <see cref="InitializeAsync"/> threw
    /// before creating it — a readiness failure, say — in which case there is nothing to drain
    /// and this method returns without touching <paramref name="context"/>, rather than throwing
    /// a <see cref="NullReferenceException"/> out of [AssemblyCleanup] that would itself become a
    /// second, unrelated failure stacked on top of whatever <see cref="InitializeAsync"/> already
    /// reported.
    /// </para>
    /// </summary>
    public static async Task CleanupAsync(TestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (RetainedFixtureContext is null)
        {
            return;
        }

        try
        {
            await FixtureRunner.DrainAsync(RetainedFixtureContext).ConfigureAwait(false);
        }
        catch (FixtureLifecycleException ex)
        {
            // RunIdValue defaults to null! rather than throwing when read unset, but an unset
            // run id must be named explicitly here rather than silently disappearing from the
            // one message that gives an operator something to search logs and a database for.
            var runId = RunIdValue ?? "unavailable (AssemblyInitialize did not complete)";

            // DrainAsync's own message already carries its remediation clause (Task 3). "This
            // run's results are unaffected" is deliberately not said: it is true, but the risk
            // worth naming is that state this run failed to tear down can break a later one.
            var message =
                $"InTest fixture cleanup failed during AssemblyCleanup for run '{runId}': {ex.Message} " +
                "State this run created may still be present and can break a later run.";

            context.WriteLine(message);
            Console.Error.WriteLine(message);
        }
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

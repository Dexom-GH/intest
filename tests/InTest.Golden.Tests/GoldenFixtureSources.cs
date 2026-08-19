namespace InTest.Golden.Tests;

/// <summary>
/// C# source for the <c>IAssemblyFixture</c> implementations the golden execution tests write
/// into a scaffolded project, the way an adopter would add their own — see each constant's own
/// doc for which test uses it and why. Extracted out of <c>GeneratedSuiteExecutionTests</c> (M7
/// of Task 6's third review round) alongside <see cref="GoldenApiStub"/>, once that file had
/// doubled in size from Task 6's own additions.
/// </summary>
internal static class GoldenFixtureSources
{
    /// <summary>
    /// An <see cref="InTest.Runtime.IAssemblyFixture"/> for
    /// <c>GeneratedSuiteExecutionTests.APublishedFixtureKeyReachesALiveRequest</c> that behaves
    /// like a real seeding fixture rather than a constant: it takes <c>IHttpClientFactory</c> as
    /// a constructor dependency — resolvable only because <c>TestHost</c> builds the service
    /// provider, and registers <c>InTestClients.Api</c> on it, before any fixture is constructed
    /// — and publishes whatever value the live call to <c>/api/seed</c> returns, rather than a
    /// value baked into source. That call only succeeds once <see cref="GoldenApiStub"/> has been
    /// probed as ready (see its <c>HandleSeed</c>), so a suite that ran seeding before readiness
    /// fails here with a real <see cref="System.Net.Http.HttpRequestException"/>, not merely a
    /// hypothetical.
    /// </summary>
    public const string SeedIdFixture = """
    using System.Net.Http;
    using System.Text.Json;
    using InTest.Runtime;

    namespace Stub.ApiTests;

    public sealed class SeedIdFixture(IHttpClientFactory httpClientFactory) : IAssemblyFixture
    {
        public Type[] DependsOn { get; } = [];
        public string[] AppliesTo { get; } = [];

        public async Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
        {
            var client = httpClientFactory.CreateClient(InTestClients.Api);
            using var response = await client.GetAsync("/api/seed", ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var seedValue = document.RootElement.GetProperty("seedValue").GetString()!;

            ctx.Publish("seededId", seedValue);
        }
    }
    """;

    /// <summary>
    /// An <see cref="InTest.Runtime.IAssemblyFixture"/> whose <c>AppliesTo</c> excludes every
    /// profile the scaffold could plausibly be running under, for
    /// <c>GeneratedSuiteExecutionTests.SkippedFixtureIsNotRunByALiveGeneratedSuite</c> to assert
    /// it was skipped rather than merely hoping: if it ran, it leaves a marker file on disk and
    /// then throws, failing the whole suite loudly instead of silently seeding the wrong thing.
    /// </summary>
    public const string SkippedFixture = """
    using InTest.Runtime;

    namespace Stub.ApiTests;

    public sealed class SkippedFixture : IAssemblyFixture
    {
        public Type[] DependsOn { get; } = [];
        public string[] AppliesTo { get; } = ["qa"];

        public Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "skipped-fixture-ran.marker"), "ran");
            throw new InvalidOperationException(
                "SkippedFixture must never run: its AppliesTo excludes the active profile.");
        }
    }
    """;

    /// <summary>
    /// An <see cref="InTest.Runtime.IAssemblyFixture"/> for
    /// <c>GeneratedSuiteExecutionTests.TheGeneratedSuitePassesTwiceAgainstTheSameStore</c> (Task
    /// 8a) — the essential create-then-clean-up pair out of <c>docs/v0-acceptance.md</c>'s v1-b
    /// <c>CatalogSeedFixture</c>, reduced to what this guard needs: <c>CatalogSeedFixture</c>'s
    /// category (created, published, and deleted on cleanup) without its product (created and
    /// deliberately never deleted, to prove a different, unrelated claim about permanent leaks
    /// that this test does not need). Creates an item this run owns via a live
    /// <c>POST /api/items</c>, publishes its id, and registers cleanup to delete it — so a second
    /// run against the same <see cref="GoldenApiStub"/> store neither collides with the first
    /// run's own seed <c>sku</c> nor tries to delete a row that already came and went. Also
    /// publishes a second, independently generated <c>sku</c> for the suite's own generated
    /// <c>CreateItem_Contract</c> test body — fresh every run, for the same reason a literal
    /// there is exactly what <see cref="GoldenApiStub"/>'s store never forgets.
    /// </summary>
    public const string RepeatableSeedFixture = """
    using System.Net;
    using System.Net.Http.Json;
    using System.Text.Json;
    using InTest.Runtime;

    namespace Stub.ApiTests;

    public sealed class RepeatableSeedFixture(IHttpClientFactory httpClientFactory) : IAssemblyFixture
    {
        public Type[] DependsOn { get; } = [];
        public string[] AppliesTo { get; } = [];

        public async Task InitializeAsync(FixtureContext ctx, CancellationToken ct)
        {
            var client = httpClientFactory.CreateClient(InTestClients.Api);

            // An item this run owns, so DeleteItem_Contract has a target that still exists on
            // every run, and so its own cleanup below proves the store does not accumulate this
            // one across runs (the "deleted rows do not come back" half of F7).
            var seedSku = $"seed-{Guid.NewGuid():N}";
            using var seedResponse = await client.PostAsJsonAsync("/api/items", new { sku = seedSku }, ct);
            seedResponse.EnsureSuccessStatusCode();
            var seeded = await seedResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var seededId = seeded.GetProperty("id").GetString()!;
            ctx.Publish("seededItem.id", seededId);

            ctx.OnCleanup(async () =>
            {
                using var response = await client.DeleteAsync($"/api/items/{seededId}", ct);
                // Tolerate 404: DeleteItem_Contract may already have deleted this exact row as
                // part of the run it was seeded for. Anything else is a real cleanup failure.
                if (response.StatusCode != HttpStatusCode.NoContent && response.StatusCode != HttpStatusCode.NotFound)
                {
                    response.EnsureSuccessStatusCode();
                }
            });

            // A second, independent sku for the suite's own POST /api/items test body — must
            // differ from the seed sku above and be fresh every run, or a second run's create
            // collides with a sku the store never forgot (the "literal values collide with
            // unique constraints" half of F7).
            ctx.Publish("newItem.sku", $"new-{Guid.NewGuid():N}");
        }
    }
    """;
}

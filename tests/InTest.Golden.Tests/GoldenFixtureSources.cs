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
    /// 8a) — <c>docs/v0-acceptance.md</c>'s v1-b <c>CatalogSeedFixture</c> reduced to what this
    /// guard needs, plus one addition a review round on Task 8a's first draft found missing.
    /// <para>
    /// <b>Seeded item.</b> <c>CatalogSeedFixture</c>'s category, unchanged: created, published,
    /// and deleted on cleanup, so a second run against the same <see cref="GoldenApiStub"/> store
    /// neither collides with the first run's own seed <c>sku</c> nor tries to delete a row that
    /// already came and went. Also publishes a second, independently generated <c>sku</c> for the
    /// suite's own generated <c>CreateItem_Contract</c> test body — fresh every run, for the same
    /// reason a literal there is exactly what a duplicate-<c>sku</c> 409 needs (the "literal
    /// values collide with unique constraints" half of F7). Neither of those two rows is ever
    /// cleaned up by anyone but their own creator here, and only the first is also targeted by a
    /// generated test (<c>DeleteItem_Contract</c>).
    /// </para>
    /// <para>
    /// <b>Cleanup-only item — the addition.</b> The first draft's cleanup for the seeded item
    /// above always tolerated a 404, because <c>DeleteItem_Contract</c> genuinely may have
    /// deleted that exact row already — but that also meant the cleanup delete's own outcome was
    /// never actually load-bearing: it could target the wrong id entirely and the guard would
    /// stay green, since nothing ever depended on <em>that specific delete</em> succeeding (only
    /// on <em>a</em> delete request going out at all, which
    /// <c>GeneratedSuiteExecutionTests.TheGeneratedSuitePassesTwiceAgainstTheSameStore</c>'s own
    /// <c>deleteCalls</c> count already checked). This second item exists solely so one cleanup
    /// delete's correctness is observable: nothing else in the generated suite ever references
    /// or deletes it, so if its cleanup does not genuinely remove it —  wrong id, no-op, anything
    /// short of a real 204 — the row is still in the store after <c>AssemblyCleanup</c>, and
    /// <see cref="GoldenApiStub.ItemCount"/> comes out one higher than expected. Its own cleanup
    /// does not tolerate 404: nothing else could legitimately have deleted this row first, so a
    /// 404 here would itself be the bug, not a benign race with a generated test.
    /// </para>
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
            // collides with a sku still live from the first (the "literal values collide with
            // unique constraints" half of F7).
            ctx.Publish("newItem.sku", $"new-{Guid.NewGuid():N}");

            // A second seeded item nothing else in the generated suite ever references or
            // deletes — so its cleanup below is the only thing that can ever remove it. Proves
            // the drain genuinely deletes the right row, not merely that a delete request went
            // out: a wrong id, or a no-op registered in its place, leaves this row behind and
            // GoldenApiStub.ItemCount comes out one higher than the test expects.
            var cleanupOnlySku = $"cleanup-only-{Guid.NewGuid():N}";
            using var cleanupOnlyResponse = await client.PostAsJsonAsync("/api/items", new { sku = cleanupOnlySku }, ct);
            cleanupOnlyResponse.EnsureSuccessStatusCode();
            var cleanupOnlyItem = await cleanupOnlyResponse.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var cleanupOnlyId = cleanupOnlyItem.GetProperty("id").GetString()!;

            ctx.OnCleanup(async () =>
            {
                // No 404 tolerance here: nothing else ever deletes this row, so a 404 would mean
                // the cleanup itself targeted the wrong id, not a benign race.
                using var response = await client.DeleteAsync($"/api/items/{cleanupOnlyId}", ct);
                response.EnsureSuccessStatusCode();
            });
        }
    }
    """;
}

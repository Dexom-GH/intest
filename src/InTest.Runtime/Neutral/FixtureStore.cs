using System.Text.Json.Nodes;

namespace InTest.Runtime;

/// <summary>
/// Raised by <see cref="FixtureStore.Get"/> for an operation with no fixture on disk. The
/// message names the repair command rather than just the missing key, because the fix is
/// always the same command and a reader should not have to know that separately.
/// </summary>
public sealed class FixtureNotFoundException(string message) : Exception(message);

/// <summary>
/// Loads every fixture under <c>{root}/fixtures/*.json</c> and, when <paramref name="profile"/>
/// is given, deep-merges any <c>{root}/fixtures/{profile}/*.json</c> overlay over it — the
/// environment wins, property by property, not object by object. <c>root</c> is the directory
/// that <em>contains</em> <c>fixtures/</c>, not <c>fixtures/</c> itself; <c>TestHost</c> passes
/// <c>AppContext.BaseDirectory</c>.
/// <para>
/// An absent <c>fixtures/</c> directory loads to an empty store rather than throwing: a spec
/// whose every operation is a parameterless GET needs no fixtures at all, and
/// <c>GeneratedSuiteExecutionTests</c> depends on that shape continuing to work.
/// </para>
/// </summary>
public sealed class FixtureStore
{
    private readonly Dictionary<string, Fixture> _fixtures;

    private FixtureStore(Dictionary<string, Fixture> fixtures) => _fixtures = fixtures;

    /// <summary>Number of operations with a loaded fixture, base and overlay combined.</summary>
    public int Count => _fixtures.Count;

    public static FixtureStore Load(string root, string? profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var fixturesDir = Path.Combine(root, "fixtures");
        var fixtures = new Dictionary<string, Fixture>(StringComparer.Ordinal);

        if (!Directory.Exists(fixturesDir)) return new FixtureStore(fixtures);

        foreach (var file in Directory.GetFiles(fixturesDir, "*.json", SearchOption.TopDirectoryOnly))
            fixtures[KeyOf(file)] = ParseFile(file);

        if (!string.IsNullOrEmpty(profile))
        {
            var overlayDir = Path.Combine(fixturesDir, profile);
            if (Directory.Exists(overlayDir))
            {
                foreach (var file in Directory.GetFiles(overlayDir, "*.json", SearchOption.TopDirectoryOnly))
                {
                    var key = KeyOf(file);
                    var fileName = Path.GetFileName(file);

                    if (!fixtures.TryGetValue(key, out var baseFixture))
                        throw new FixtureFormatException(
                            $"fixtures/{profile}/{fileName} overlays an operation with no base " +
                            $"fixture 'fixtures/{fileName}'. Run `intest fixtures repair` first, " +
                            "or remove the overlay.");

                    fixtures[key] = Merge(baseFixture, ParseFile(file));
                }
            }
        }

        return new FixtureStore(fixtures);
    }

    /// <summary>
    /// The raw fixture, tokens unresolved. Startup validation (Task 7) inspects tokens, so it
    /// must call this rather than a resolving accessor.
    /// </summary>
    public Fixture Get(string key)
    {
        if (_fixtures.TryGetValue(key, out var fixture)) return fixture;
        throw new FixtureNotFoundException(
            $"No fixture is defined for operation '{key}'. Run `intest fixtures repair` to generate one.");
    }

    private static string KeyOf(string path) => Path.GetFileNameWithoutExtension(path);

    private static Fixture ParseFile(string path)
    {
        try
        {
            return Fixture.Parse(File.ReadAllText(path));
        }
        catch (FixtureFormatException ex)
        {
            // Fixture.Parse knows the offending field but not the file it came from — only the
            // caller iterating the directory knows that, so the filename is added here.
            throw new FixtureFormatException($"{Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    private static Fixture Merge(Fixture baseFixture, Fixture overlay)
    {
        var parameters = new SortedDictionary<string, string>(baseFixture.Parameters, StringComparer.Ordinal);
        foreach (var (key, value) in overlay.Parameters) parameters[key] = value;

        return new Fixture
        {
            Parameters = parameters,
            Body = MergeBody(baseFixture.Body, overlay.Body)
        };
    }

    /// <summary>
    /// Merges per property rather than replacing the object wholesale: an overlay that overrides
    /// one nested property must leave its siblings — from either side — untouched.
    /// </summary>
    private static JsonNode? MergeBody(JsonNode? baseBody, JsonNode? overlayBody)
    {
        if (overlayBody is null) return baseBody?.DeepClone();
        if (baseBody is not JsonObject baseObj || overlayBody is not JsonObject overlayObj)
            return overlayBody.DeepClone();

        var merged = new JsonObject();
        foreach (var (key, value) in baseObj) merged[key] = value?.DeepClone();

        foreach (var (key, value) in overlayObj)
        {
            var baseChild = merged.TryGetPropertyValue(key, out var existing) ? existing : null;
            merged[key] = baseChild is JsonObject && value is JsonObject
                ? MergeBody(baseChild, value)
                : value?.DeepClone();
        }

        return merged;
    }
}

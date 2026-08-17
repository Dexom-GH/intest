using System.Text.Json.Nodes;

namespace InTest.Cli.Fixtures;

/// <summary>
/// The result of comparing a committed fixture against what <see cref="FixtureComposer"/> would
/// compose from the current schema. Never mutates either document — <see cref="Commands.FixturesRepairCommand"/>
/// decides what to do with each list.
/// </summary>
public sealed record FixtureDriftResult(
    IReadOnlyList<string> MissingProperties,
    IReadOnlyList<string> StaleProperties,
    IReadOnlyList<string> MissingParameters);

/// <summary>
/// Compares an existing, possibly hand-edited fixture against a freshly composed one for the
/// same operation. Read-only by design: it reports what changed on either side, and leaves the
/// decision of what to do about it — add, retain, report — to the caller (§10, decision in
/// Task 3's plan section: repair never overwrites a value a human wrote).
/// </summary>
public static class FixtureDrift
{
    public static FixtureDriftResult Compare(FixtureDocument existing, FixtureDocument composed)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(composed);

        var existingProperties = PropertyNames(existing.Body);
        var composedProperties = PropertyNames(composed.Body);

        var missingProperties = composedProperties
            .Where(name => !existingProperties.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var staleProperties = existingProperties
            .Where(name => !composedProperties.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var missingParameters = composed.Parameters.Keys
            .Where(name => !existing.Parameters.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        return new FixtureDriftResult(missingProperties, staleProperties, missingParameters);
    }

    /// <summary>
    /// Top-level property names of a JSON object body, or empty for an operation with no body
    /// (or a body that is not an object — nothing to diff property-by-property in that case).
    /// </summary>
    private static HashSet<string> PropertyNames(JsonNode? body) =>
        body is JsonObject obj ? obj.Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal) : [];
}

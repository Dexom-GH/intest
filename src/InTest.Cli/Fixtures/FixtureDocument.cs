using System.Text.Json;
using System.Text.Json.Nodes;

namespace InTest.Cli.Fixtures;

public sealed class FixtureFormatException(string message) : Exception(message);

public sealed class FixtureMeta
{
    public required int Tier { get; init; }
    public required string OperationId { get; init; }
    public required string GeneratedBy { get; init; }
}

/// <summary>
/// One fixture per operation: its path and query parameters, and its request body if it takes
/// one. Committed, hand-edited, and never overwritten by tooling once written.
/// </summary>
public sealed class FixtureDocument
{
    public required FixtureMeta Meta { get; init; }
    public SortedDictionary<string, string> Parameters { get; init; } = new(StringComparer.Ordinal);
    public JsonNode? Body { get; set; }

    private static readonly string[] ReservedNames =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7",
         "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    /// <summary>
    /// Explicit, not <see cref="Path.GetInvalidFileNameChars"/> alone: that call returns 41
    /// characters on Windows but only NUL and '/' on Unix, so a Windows dev box or CI agent
    /// cannot observe this list shrinking — only a direct assertion on the list itself can.
    /// Internal and exposed to InTest.Cli.Tests via InternalsVisibleTo for exactly that reason.
    /// </summary>
    internal static readonly char[] InvalidOperationKeyCharacters =
        ['/', '\\', '?', '*', ':', '"', '<', '>', '|'];

    /// <summary>
    /// Operation keys become fixture filenames. Synthesized keys are safe by construction, but
    /// a declared operationId is used verbatim and OpenAPI permits any string.
    /// <para>
    /// Returns false with a reason rather than throwing, because an unusable operationId is one
    /// operation InTest cannot serve — not grounds for abandoning a whole document. The caller
    /// records a skip and carries on, the same route non-JSON request bodies already take.
    /// </para>
    /// </summary>
    public static bool TryValidateOperationKey(string operationKey, out string reason)
    {
        if (string.IsNullOrWhiteSpace(operationKey))
        {
            reason = "operationId is empty.";
            return false;
        }

        var invalid = InvalidOperationKeyCharacters.Concat(Path.GetInvalidFileNameChars()).ToHashSet();

        var offending = operationKey.Where(invalid.Contains).Distinct().ToArray();
        if (offending.Length > 0)
        {
            reason = $"operationId '{operationKey}' cannot be a fixture filename: it contains " +
                     $"{string.Join(", ", offending.Select(c => $"'{c}'"))}. Change the operationId " +
                     "in the OpenAPI document — it also names generated client methods, so a " +
                     "filename-safe value is worth having anyway.";
            return false;
        }

        if (ReservedNames.Contains(operationKey, StringComparer.OrdinalIgnoreCase))
        {
            reason = $"operationId '{operationKey}' is a reserved device name on Windows and cannot " +
                     "be a filename. Change the operationId in the OpenAPI document.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Only valid for a key that has already passed <see cref="TryValidateOperationKey"/>.
    /// Throws otherwise, because reaching here with an unusable key means a caller skipped
    /// validation — an invariant violation rather than a condition to handle.
    /// </summary>
    public static string FileNameFor(string operationKey)
    {
        if (!TryValidateOperationKey(operationKey, out var reason))
            throw new FixtureFormatException(reason);

        return operationKey + ".json";
    }

    public string ToJson()
    {
        var root = new JsonObject
        {
            ["$meta"] = new JsonObject
            {
                ["tier"] = Meta.Tier,
                ["operationId"] = Meta.OperationId,
                ["generatedBy"] = Meta.GeneratedBy
            }
        };

        if (Parameters.Count > 0)
        {
            var parameters = new JsonObject();
            foreach (var (key, value) in Parameters) parameters[key] = value;
            root["$parameters"] = parameters;
        }

        if (Body is not null) root["body"] = Body.DeepClone();

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    public static FixtureDocument Parse(string json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException ex) { throw new FixtureFormatException($"Fixture is not valid JSON: {ex.Message}"); }

        if (root is not JsonObject obj) throw new FixtureFormatException("Fixture root must be a JSON object.");

        if (obj["$meta"] is not JsonObject meta)
            throw new FixtureFormatException("Fixture is missing its '$meta' block. Regenerate it with `intest fixtures repair`.");

        var document = new FixtureDocument
        {
            Meta = new FixtureMeta
            {
                Tier = meta["tier"]?.GetValue<int>() ?? 4,
                OperationId = meta["operationId"]?.GetValue<string>() ?? string.Empty,
                GeneratedBy = meta["generatedBy"]?.GetValue<string>() ?? "unknown"
            },
            Body = obj["body"]?.DeepClone()
        };

        if (obj["$parameters"] is JsonObject parameters)
        {
            foreach (var (key, value) in parameters)
                document.Parameters[key] = value?.GetValue<string>() ?? string.Empty;
        }

        return document;
    }
}

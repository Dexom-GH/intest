namespace InTest.Runtime;

/// <summary>
/// v0 path-parameter values. Fails loudly rather than substituting a plausible value,
/// because a permissive endpoint would accept junk and the suite would assert nothing.
/// v1 replaces this with fixture-backed lookups.
/// </summary>
public static class TestData
{
    private static readonly Dictionary<string, string> Values = new(StringComparer.Ordinal);

    public static void Set(string operationKey, string parameterName, string value)
        => Values[$"{operationKey}:{parameterName}"] = value;

    public static string Require(string operationKey, string parameterName)
        => Values.TryGetValue($"{operationKey}:{parameterName}", out var value)
            ? value
            : throw new InvalidOperationException(
                $"No test data for '{parameterName}' on operation '{operationKey}'. " +
                $"Register it in TestStartup with TestData.Set(\"{operationKey}\", \"{parameterName}\", \"…\").");
}

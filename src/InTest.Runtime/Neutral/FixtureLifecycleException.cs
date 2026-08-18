namespace InTest.Runtime;

/// <summary>
/// Raised for a problem in *running* fixtures — a dependency cycle, a key published twice, a
/// fixture that throws, or teardown that throws — as opposed to <see cref="FixtureResolutionException"/>,
/// which is reserved for an unresolvable <c>{{...}}</c> token. The split matters because
/// <c>FixtureValidation.CheckLeaf</c> catches only <see cref="FixtureResolutionException"/>: an
/// unpublished <c>{{fixture:...}}</c> key must stay a resolution failure so it is aggregated into
/// the startup report like any other bad token, while a lifecycle problem is not something that
/// report was ever built to absorb — it means the fixtures never finished running at all, so it
/// must fail loudly rather than turn into one more line in a table of resolved-but-blocked
/// operations.
/// </summary>
public sealed class FixtureLifecycleException(string message) : Exception(message);

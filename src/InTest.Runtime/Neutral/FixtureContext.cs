namespace InTest.Runtime;

/// <summary>
/// The state one assembly run's fixtures publish into and register teardown against. One
/// instance is created by <c>TestHost</c>, passed to every <see cref="IAssemblyFixture"/>, and
/// retained in a static field so <c>AssemblyCleanup</c> can drain the same instance the fixtures
/// wrote to (decision 4). This type only records — it runs nothing itself; <see cref="FixtureRunner"/>
/// (Task 3) owns ordering fixtures, invoking them, and draining <see cref="CleanupActions"/>.
/// </summary>
public sealed class FixtureContext
{
    private readonly Dictionary<string, string> _published = new(StringComparer.Ordinal);
    private readonly List<Func<Task>> _cleanupActions = [];

    /// <summary>
    /// Every published key, ordinal-sorted. A fixed order regardless of publish order or machine
    /// locale keeps any message that lists them — a cycle report, a duplicate-key error —
    /// reproducible from one run to the next.
    /// </summary>
    public IReadOnlyList<string> PublishedKeys => _published.Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Teardown registered so far, in registration order — the order <see cref="FixtureRunner"/>
    /// must drain in reverse (decision 4).
    /// </summary>
    public IReadOnlyList<Func<Task>> CleanupActions => _cleanupActions;

    /// <summary>
    /// Makes <paramref name="value"/> available to <c>{{fixture:...}}</c> tokens under
    /// <paramref name="key"/>. Publishing the same key twice throws rather than overwriting —
    /// a silent overwrite would make token resolution depend on which fixture happened to run
    /// last, exactly the non-determinism topological ordering exists to remove.
    /// </summary>
    public void Publish(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        if (!_published.TryAdd(key, value))
        {
            throw new FixtureLifecycleException(
                $"Fixture key '{key}' was already published. Each key may be published once.");
        }
    }

    /// <summary>The value published under <paramref name="key"/>. Task 4 wires this to <c>{{fixture:...}}</c>.</summary>
    public string Get(string key) => _published[key];

    /// <summary>
    /// Records <paramref name="action"/> to run during teardown, next to whatever created the
    /// thing it cleans up. Recording never runs it — <see cref="FixtureRunner"/> decides when,
    /// draining every registered action in reverse.
    /// </summary>
    public void OnCleanup(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _cleanupActions.Add(action);
    }
}

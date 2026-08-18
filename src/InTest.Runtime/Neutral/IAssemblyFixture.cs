namespace InTest.Runtime;

/// <summary>
/// A team-written seed for one assembly run — registered with
/// <c>services.AddSingleton&lt;IAssemblyFixture, ...&gt;()</c> in <c>TestStartup.cs</c>, never
/// discovered by reflection (decision 2), so ordering and enablement stay explicit rather than a
/// side effect of which classes happen to exist. <see cref="FixtureRunner"/> (Task 3) topologically
/// orders every registered fixture over <see cref="DependsOn"/>, then calls
/// <see cref="InitializeAsync"/> on each in turn. There is deliberately no matching cleanup
/// method here — §13 registers teardown next to whatever created the thing, via
/// <see cref="FixtureContext.OnCleanup"/>, rather than through a second lifecycle method a team
/// would have to remember to keep in sync with the first.
/// </summary>
public interface IAssemblyFixture
{
    /// <summary>
    /// Other fixture types that must finish <see cref="InitializeAsync"/> before this one starts.
    /// A cycle, or a dependency on a type nobody registered, fails <c>AssemblyInitialize</c> by
    /// name (decision 3) rather than running in whatever order reflection happened to produce.
    /// </summary>
    Type[] DependsOn { get; }

    /// <summary>
    /// The operation keys this fixture's data is relevant to, or empty to apply to every
    /// operation. Purely informational for <see cref="FixtureRunner"/> today; it exists so a
    /// fixture's intent is documented next to its seeding code rather than only in a comment.
    /// </summary>
    string[] AppliesTo { get; }

    /// <summary>
    /// Seeds data and publishes whatever <c>{{fixture:...}}</c> tokens need, via
    /// <paramref name="ctx"/>. Runs after readiness (decision 1), so an HTTP client to the
    /// service under test is available; runs before <c>TokenResolver</c> is built, so
    /// publishing here is what makes <c>{{fixture:...}}</c> resolvable at all.
    /// </summary>
    Task InitializeAsync(FixtureContext ctx, CancellationToken ct);
}

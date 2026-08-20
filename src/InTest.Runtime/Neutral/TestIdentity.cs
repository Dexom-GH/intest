namespace InTest.Runtime;

/// <summary>
/// One identity an <see cref="ITestTokenProvider"/> can issue tokens for: the name
/// <see cref="ITestTokenProvider.GetTokenAsync"/> selects it by, and the scopes it holds.
/// Replaces the bare identity-name strings <see cref="ITestTokenProvider.Identities"/> used to
/// carry, so that a later scope-aware guard can read both from one descriptor instead of a
/// parallel lookup keyed by the same strings — a lookup that could disagree with itself.
/// <para>
/// Names are expected unique within a single <see cref="ITestTokenProvider.Identities"/> list;
/// behaviour is undefined otherwise. The runtime does not validate this — policing a provider's
/// own data is not its job.
/// </para>
/// </summary>
/// <param name="Name">What callers pass as <see cref="ITestTokenProvider.GetTokenAsync"/>'s
/// <c>identity</c> parameter to select this identity.</param>
/// <param name="Scopes">
/// The scopes this identity holds. <c>null</c> means not declared / unknown. An empty collection
/// is itself a declaration — this identity holds no scopes — and is a different state from
/// <c>null</c>, not an equivalent one: collapsing the two would make every undeclared identity
/// look like a deliberate declaration. Non-empty is the scopes it holds.
/// </param>
public sealed record TestIdentity(string Name, IReadOnlyCollection<string>? Scopes = null);

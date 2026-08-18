namespace InTest.Runtime;

/// <summary>
/// Raised when a <c>{{...}}</c> token in a fixture cannot be resolved — an unknown token name, a
/// <c>{{fixture:...}}</c> token before v1-b (decision 4), or a <c>{{config:}}</c>/<c>{{secret:}}</c>
/// key with no configured value. Every message is built from the token's <em>name</em> only, never
/// a resolved value, so a secret that resolved successfully earlier in the same fixture cannot
/// leak into the message for a different, later failure in that same fixture.
/// </summary>
public sealed class FixtureResolutionException(string message) : Exception(message);
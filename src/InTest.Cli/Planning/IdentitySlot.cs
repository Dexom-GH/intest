namespace InTest.Cli.Planning;

/// <summary>
/// Which identity a generated auth case authenticates as (decision 7) — never a literal identity
/// name, since the CLI generates this plan long before any adopter has written an
/// <c>ITestTokenProvider</c> and cannot know one. Mirrors <c>InTest.Runtime.IdentitySlot</c> by
/// name only: this project does not, and must not, reference <c>InTest.Runtime</c> (the CLI
/// generates code for a project that references it, it does not consume it), so
/// <see cref="Rendering.TemplateRenderer"/> is what turns a value here into the literal
/// <c>IdentitySlot.Whatever</c> text the rendered method body names — the generated code's own
/// <c>using InTest.Runtime;</c> is what makes that symbol resolve there.
/// </summary>
public enum IdentitySlot
{
    /// <summary>No override: the ambient identity <c>ApiTestBase.ApiTestInitialize</c> already
    /// set. Every case that is not <see cref="CaseRole.Auth"/> carries this by default and the
    /// template emits nothing for it — the reason every existing success case stays
    /// byte-identical in the golden file once <see cref="CaseRole.Auth"/> exists.</summary>
    Default,

    /// <summary>Some other identity than <see cref="Default"/> — the wrong-scope 403 case.</summary>
    Secondary,

    /// <summary>Send no token at all — the no-token 401 case.</summary>
    None
}
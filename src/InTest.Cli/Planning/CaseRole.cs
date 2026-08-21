namespace InTest.Cli.Planning;

/// <summary>
/// What a case's <see cref="TestCasePlan.ExpectedStatus"/> represents. Declared errors are never
/// inferred (decision 5) — a case exists in this role only because the spec itself declared the
/// response InTest generated it from. <see cref="Auth"/> (Task 5) is different again: it is never
/// read off a declared response either — it exists because the operation declares `security`, and
/// its expected status (401 or 403) comes from decision 3's fixed pair, not from anything the spec
/// enumerates in `responses`. v1-c generates only these three.
/// </summary>
public enum CaseRole
{
    Success,
    DeclaredError,

    /// <summary>
    /// A no-token 401 case or a wrong-scope 403 case (decision 3), generated once each for every
    /// operation that declares `security` — independent of whether the operation's own
    /// `responses` enumerate 401 or 403 at all. Carries an <see cref="TestCasePlan.Slot"/>
    /// (decision 7) selecting which identity the generated case authenticates as:
    /// <see cref="IdentitySlot.None"/> for the 401 case, <see cref="IdentitySlot.Secondary"/> for
    /// the 403 case. Like <see cref="DeclaredError"/>, always fixture-free and pointed at an
    /// unmatchable id (decision 6) — see <see cref="TestCasePlan.NeedsFixture"/> and
    /// <see cref="TestCasePlan.PathParameterKinds"/> on this role's cases.
    /// </summary>
    Auth
}
namespace InTest.Cli.Planning;

/// <summary>
/// The OpenAPI-declared shape of a path parameter, as far as <see cref="TemplateRenderer"/>
/// needs to know it to pick an unmatchable-but-well-typed value for a non-success case (decision
/// 6). Review finding on Task 4: rendering <c>Guid.NewGuid().ToString()</c> for every path
/// parameter regardless of declared type sends an ill-typed value against a `type: integer`
/// parameter — an ASP.NET Core `[ApiController]` binding `int id` without a route constraint
/// answers 400 from model binding before the action's <c>NotFound()</c> path ever runs, so the
/// generated 404 case fails on every run. Only <see cref="Integer"/> gets special treatment;
/// every other declared type (string, its formats included, or nothing declared at all) still
/// takes a fresh GUID, which was already a well-typed unmatchable value for those.
/// </summary>
public enum PathParameterKind
{
    String,
    Integer
}
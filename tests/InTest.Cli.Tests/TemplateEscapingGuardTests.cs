using System.Reflection;
using System.Text.RegularExpressions;
using InTest.Cli.Rendering;
using Shouldly;

namespace InTest.Cli.Tests;

/// <summary>
/// Guards the recurrence direction the original defect actually took: an unescaped,
/// spec-derived value quoted directly into mstest-class.scriban. The '_literal' naming
/// convention (see TemplateRenderer.RenderClass's own comment) only helps a reader who already
/// knows to look for it — it does nothing to stop a future template edit from quoting a new
/// field that was never routed through CSharpLiteral.Escape in the first place. This test reads
/// the template itself and enforces the naming convention mechanically: every field the
/// template puts inside a quoted <c>{{ tc.name }}</c> interpolation must either be named
/// '_literal' or be explicitly allow-listed below with a reason.
/// <para>
/// Deliberately does not attempt to verify that a '_literal' field is <i>actually</i> escaped in
/// TemplateRenderer.cs — that would mean re-implementing a small C# parser to trace model
/// construction, which is not worth it here. This test only enforces the naming discipline that
/// TemplateRendererEscapingTests then exercises for real by rendering hostile input through
/// each field and asserting the escaped output. The two together cover both halves: this one
/// catches "a new quoted site was never named or escaped at all", the other catches "the escape
/// function itself is wrong".
/// </para>
/// </summary>
[TestClass]
public class TemplateEscapingGuardTests
{
    /// <summary>
    /// Field names allowed to appear inside a quoted <c>{{ tc.name }}</c> interpolation without
    /// a '_literal' suffix, and why each is safe there. Add to this list only for a field that
    /// truly cannot carry spec text — anything else belongs in TemplateRenderer.RenderClass's
    /// model, escaped and named with a '_literal' suffix instead.
    /// </summary>
    private static readonly HashSet<string> AllowedUnescapedFields = new(StringComparer.Ordinal)
    {
        // Always the constant TestPlanBuilder.ContractCategory = "Contract"
        // (TestPlanBuilder.cs:12) — never spec-derived, so there is no spec text here for
        // CSharpLiteral.Escape to act on.
        "category",
    };

    private static readonly Regex QuotedInterpolation =
        new("\"\\{\\{\\s*tc\\.(\\w+)\\s*\\}\\}\"", RegexOptions.Compiled);

    [TestMethod]
    public void EveryQuotedTemplateFieldIsEscapedOrExplicitlyAllowed()
    {
        var template = LoadEmbeddedTemplate("mstest-class.scriban");
        var matches = QuotedInterpolation.Matches(template);

        // If this is ever zero, the regex has stopped matching the template's actual syntax
        // (a Scriban formatting change, say) — that is this guard silently going blind, not a
        // clean bill of health, so it must fail loudly rather than pass vacuously.
        matches.Count.ShouldBeGreaterThan(0,
            "the quoted-interpolation regex matched nothing in mstest-class.scriban. Either the " +
            "template's quoting syntax changed and the regex in TemplateEscapingGuardTests needs " +
            "updating, or this guard is passing vacuously — do not leave it silently disabled.");

        var offenders = matches
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .Where(name => !name.EndsWith("_literal", StringComparison.Ordinal) && !AllowedUnescapedFields.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "mstest-class.scriban quotes {{ tc." + string.Join(" }}, {{ tc.", offenders) + " }} " +
            "without a '_literal' suffix. If the field's value can carry spec-derived text, apply " +
            "CSharpLiteral.Escape to it where TemplateRenderer.RenderClass builds the model and " +
            "rename it with a '_literal' suffix. If it genuinely never carries spec text (like " +
            "'category' today), add it to TemplateEscapingGuardTests.AllowedUnescapedFields with " +
            "a one-line reason instead.");
    }

    private static string LoadEmbeddedTemplate(string fileName)
    {
        var assembly = typeof(TemplateRenderer).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith(fileName, StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

using Shouldly;

namespace InTest.Architecture.Tests;

/// <summary>
/// §3 requires the neutral layers to name no MSTest type, so xUnit and NUnit stay additive
/// rather than becoming a rewrite. Source-level rather than reflection-based, because the
/// rule is about what the code says, not what survives compilation.
/// </summary>
[TestClass]
public class NeutralityTests
{
    private const string ForbiddenNamespace = "Microsoft.VisualStudio.TestTools.UnitTesting";

    private static string NeutralDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "InTest.sln")))
        {
            dir = dir.Parent;
        }
        dir.ShouldNotBeNull("Could not locate the repository root (InTest.sln).");
        return Path.Combine(dir!.FullName, "src", "InTest.Runtime", "Neutral");
    }

    [TestMethod]
    public void NeutralSourcesDoNotReferenceMSTest()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(NeutralDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (text.Contains(ForbiddenNamespace, StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        offenders.ShouldBeEmpty(
            $"These files are in the neutral layer but reference {ForbiddenNamespace}. " +
            "Move them under src/InTest.Runtime/MSTest/, or remove the dependency. See §3.");
    }

    [TestMethod]
    public void NeutralDirectoryIsNotEmpty()
    {
        Directory.EnumerateFiles(NeutralDirectory(), "*.cs", SearchOption.AllDirectories)
                 .ShouldNotBeEmpty("The neutrality test would pass vacuously against an empty directory.");
    }
}

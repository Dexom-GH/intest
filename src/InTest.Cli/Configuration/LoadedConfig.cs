namespace InTest.Cli.Configuration;

/// <summary>
/// The settings <c>intest.json</c> carries that a command actually reads, after validation.
/// Every property is non-null and known-good: a <see cref="LoadedConfig"/> only exists because
/// <see cref="ConfigLoader.Load"/> did not throw.
/// </summary>
public sealed record LoadedConfig(string SpecSource, string RootNamespace, string TestBaseClass);

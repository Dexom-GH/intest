namespace InTest.Cli.Spec;

public sealed class SpecLoadException(string message, Exception? inner = null) : Exception(message, inner);
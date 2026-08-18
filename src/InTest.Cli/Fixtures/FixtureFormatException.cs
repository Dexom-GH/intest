namespace InTest.Cli.Fixtures;

public sealed class FixtureFormatException(string message, Exception? inner = null) : Exception(message, inner);
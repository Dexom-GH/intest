namespace InTest.Cli.Configuration;

public sealed class ConfigLoadException(string message, Exception? inner = null) : Exception(message, inner);

namespace InTest.Runtime;

/// <summary>
/// Ambient per-test state. This must be AsyncLocal rather than a DI-scoped service:
/// handlers created by IHttpClientFactory are not scoped to the DI scope, so a scoped
/// service cannot be injected into one.
/// </summary>
public static class InTestAmbient
{
    public static readonly AsyncLocal<string?> TestId = new();
}

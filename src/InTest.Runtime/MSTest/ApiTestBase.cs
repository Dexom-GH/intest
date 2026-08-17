using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace InTest.Runtime;

/// <summary>
/// Ambient context for generated tests: configuration, services, client, identifiers and
/// scope lifecycle. Deliberately nothing domain-specific — base classes in test projects
/// become dumping grounds, so helpers belong in the team's own base class.
/// </summary>
public abstract class ApiTestBase
{
    private IServiceScope _scope = null!;

    public TestContext TestContext { get; set; } = null!;

    protected IConfiguration Config => TestHost.Configuration;
    protected IServiceProvider Services => _scope.ServiceProvider;
    protected SchemaBundle Schemas => TestHost.Schemas;
    protected string RunId => TestHost.RunIdValue;

    /// <summary>
    /// Derived from TestDisplayName, never TestName: TestName returns the bare method name
    /// for every [DataRow] row, so all variations of an operation would share one id.
    /// </summary>
    protected string TestId => InTestId.ForTest(TestHost.RunIdValue, TestContext.TestDisplayName);

    protected HttpClient Client { get; private set; } = null!;

    [TestInitialize]
    public void ApiTestInitialize()
    {
        _scope = TestHost.Root.CreateScope();
        InTestAmbient.TestId.Value = TestId;
        Client = _scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(InTestClients.Api);
    }

    [TestCleanup]
    public void ApiTestCleanup()
    {
        InTestAmbient.TestId.Value = null;
        _scope.Dispose();
    }
}

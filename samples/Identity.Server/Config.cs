using Duende.IdentityServer.Models;

namespace Identity.Server;

/// <summary>
/// In-memory configuration. Appropriate for a test fixture: deterministic, no database, and
/// every secret is a published constant precisely because none of it protects anything.
/// </summary>
public static class Config
{
    public const string ReadScope = "orders.read";
    public const string WriteScope = "orders.write";
    public const string Audience = "orders-api";

    /// <summary>Shared by every client. Public on purpose — this issues tokens for a sample.</summary>
    public const string SharedSecret = "sample-secret-not-a-real-credential";

    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new(ReadScope, "Read orders"),
        new(WriteScope, "Create and modify orders")
    ];

    public static IEnumerable<ApiResource> ApiResources =>
    [
        new(Audience, "Orders API") { Scopes = { ReadScope, WriteScope } }
    ];

    /// <summary>
    /// Two clients, and that is the entire point. A provider advertising more than one
    /// identity is what turns InTest's wrong-scope 403 tests on — with a single identity
    /// they gate off by construction.
    /// </summary>
    public static IEnumerable<Client> Clients =>
    [
        new()
        {
            ClientId = "orders-client",
            ClientName = "Full access",
            AllowedGrantTypes = GrantTypes.ClientCredentials,
            ClientSecrets = { new Secret(SharedSecret.Sha256()) },
            AllowedScopes = { ReadScope, WriteScope }
        },
        new()
        {
            ClientId = "orders-readonly",
            ClientName = "Read only — used to prove write endpoints return 403",
            AllowedGrantTypes = GrantTypes.ClientCredentials,
            ClientSecrets = { new Secret(SharedSecret.Sha256()) },
            AllowedScopes = { ReadScope }
        }
    ];
}

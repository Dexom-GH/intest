using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Orders.Api.OpenApi;

/// <summary>
/// Declares <c>security</c> on the operations that actually require it, with the scope each
/// one needs.
/// <para>
/// This matters for InTest: auth contract tests are generated per operation that declares
/// <c>security</c>, so a document-level-only declaration would produce none, and an
/// operation that needs the write scope must say so or the wrong-scope 403 test has nothing
/// to assert.
/// </para>
/// <para>
/// Implemented as a document filter rather than an operation filter for two measured
/// reasons. First, <c>OpenApiSecuritySchemeReference</c> serializes to an empty object
/// unless it is given the host document — which is why Swashbuckle v10 changed
/// <c>AddSecurityRequirement</c> to take a <c>Func&lt;OpenApiDocument, …&gt;</c> — and an
/// operation filter has no access to it. Second, endpoint metadata is not populated during
/// build-time document generation, so policies must come from reflection over the action.
/// </para>
/// </summary>
public sealed class AuthorizeOperationFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(swaggerDoc);
        ArgumentNullException.ThrowIfNull(context);

        foreach (var description in context.ApiDescriptions)
        {
            if (description.ActionDescriptor is not ControllerActionDescriptor action) continue;

            var scope = RequiredScope(action);
            if (scope is null) continue;

            var path = "/" + description.RelativePath?.TrimStart('/');
            if (!swaggerDoc.Paths.TryGetValue(path, out var pathItem)) continue;


            var method = new HttpMethod(description.HttpMethod ?? "GET");
            if (pathItem.Operations is null || !pathItem.Operations.TryGetValue(method, out var operation)) continue;

            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("bearer", swaggerDoc)] = [scope]
                }
            ];
        }
    }

    private static string? RequiredScope(ControllerActionDescriptor action)
    {
        var methodAttributes = action.MethodInfo.GetCustomAttributes(inherit: true);
        if (methodAttributes.OfType<IAllowAnonymous>().Any()) return null;

        var typeAttributes = action.ControllerTypeInfo.GetCustomAttributes(inherit: true);

        var policies = methodAttributes.Concat(typeAttributes)
                                       .OfType<AuthorizeAttribute>()
                                       .Select(a => a.Policy)
                                       .Where(p => !string.IsNullOrEmpty(p))
                                       .Select(p => p!)
                                       .ToHashSet(StringComparer.Ordinal);

        if (policies.Count == 0) return null;

        // Most specific wins: an action-level [Authorize(Write)] on a controller marked
        // [Authorize(Read)] genuinely needs the write scope.
        return policies.Contains(Policies.Write) ? Policies.Write : Policies.Read;
    }
}

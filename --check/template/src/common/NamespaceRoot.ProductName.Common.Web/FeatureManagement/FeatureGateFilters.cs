using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.Mvc;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using NamespaceRoot.ProductName.Common.Application.FeatureManagement;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NamespaceRoot.ProductName.Common.Web.FeatureManagement;

/// <summary>
/// Keeps an action behind a closed <c>[FeatureGate]</c> out of the Swagger document, as it is out of the API.
/// </summary>
/// <remarks>
/// <para>
/// While its flag is off, a gated action answers 404, and the document listed it anyway: a client generated
/// from the document got a method that cannot succeed. The operation filter sees each action's method and
/// marks the operation whose gate is closed; the document filter removes the marked operations, and the
/// paths left without one. The document is built on each request, so it follows the flags as they change.
/// </para>
/// <para>
/// A gate is read as the library reads it: <see cref="RequirementType.All"/> or <see cref="RequirementType.Any"/>
/// of its features, inverted by <see cref="FeatureGateAttribute.Negate"/>, and every gate on the action and on
/// its controller has to be open. The flags come from <see cref="IFeatureCatalog"/>; a service without it
/// keeps every operation.
/// </para>
/// </remarks>
internal sealed class FeatureGateFilters(IServiceProvider services) : IOperationFilter, IDocumentFilter
{
    private const string Closed = "x-feature-gate-closed";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo is not { } method || services.GetService<IFeatureCatalog>() is not { } catalog)
            return;

        var gates = method.GetCustomAttributes<FeatureGateAttribute>(inherit: true)
            .Concat(method.DeclaringType?.GetCustomAttributes<FeatureGateAttribute>(inherit: true) ?? []);
        if (gates.Any(gate => !IsOpen(gate, catalog)))
            operation.Extensions[Closed] = new OpenApiBoolean(true);
    }

    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        foreach (var (path, item) in document.Paths.ToList())
        {
            foreach (var (type, operation) in item.Operations.ToList())
            {
                if (operation.Extensions.ContainsKey(Closed))
                    item.Operations.Remove(type);
            }

            if (item.Operations.Count == 0)
                document.Paths.Remove(path);
        }
    }

    private static bool IsOpen(FeatureGateAttribute gate, IFeatureCatalog catalog)
    {
        var open = gate.RequirementType == RequirementType.All
            ? gate.Features.All(catalog.IsEnabled)
            : gate.Features.Any(catalog.IsEnabled);
        return gate.Negate ? !open : open;
    }
}

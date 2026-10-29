using System.Reflection;
using Microsoft.OpenApi.Models;
using NamespaceRoot.ProductName.Common.Web.Authorization;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NamespaceRoot.ProductName.Common.Web.Swagger.Filters;

/// <summary>
/// Adds required permissions from <see cref="RequiredPermissionsAttribute"/> to Swagger operation descriptions.
/// </summary>
public class PermissionsOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var permissions = context.MethodInfo
            .GetCustomAttributes<RequiredPermissionsAttribute>(true)
            .SelectMany(attr => attr.Permissions)
            .Distinct()
            .ToList();

        if (permissions.Count == 0) return;

        var permissionsText = $"\n\n---\n**Required permissions:** `{string.Join("`, `", permissions)}`\n";

        operation.Description = string.IsNullOrEmpty(operation.Description)
            ? permissionsText
            : operation.Description + permissionsText;
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NamespaceRoot.ProductName.Common.Web.Swagger.Filters;

/// <summary>
/// Adds authentication method descriptions to authorized endpoints in Swagger docs.
/// </summary>
public class AuthorizationOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var hasAuthorize = context.MethodInfo.DeclaringType?
            .GetCustomAttributes(true)
            .Union(context.MethodInfo.GetCustomAttributes(true))
            .Any(a => a is AuthorizeAttribute) ?? false;

        if (!hasAuthorize) return;

        operation.Description = $"{operation.Description}\n\n" +
                                "**Authentication Methods:**\n" +
                                $"1. **JWT Bearer Token** - `{AuthHeaders.Authorization}: Bearer {{token}}`\n" +
                                $"2. **API Key** - `{AuthHeaders.ApiKey}: {{api-key}}`\n\n" +
                                "**API Key Types:**\n" +
                                "- **User API Key**: Validates against database\n" +
                                "- **Internal API Key**: From application configuration\n\n" +
                                "**For Internal API Key with User Context:**\n" +
                                $"- `{AuthHeaders.UserId}` (required)\n" +
                                $"- `{AuthHeaders.UserLogin}` (optional)\n" +
                                $"- `{AuthHeaders.UserDisplayName}` (optional)\n" +
                                $"- `{AuthHeaders.TenantId}` (optional)\n" +
                                $"- `{AuthHeaders.UserRoles}` (optional, comma-separated)\n";
    }
}

using System.Reflection;
using Microsoft.AspNetCore.Mvc;

namespace NamespaceRoot.ProductName.Common.Tests.Rules;

/// <summary>
/// Finds a token in what a controller action returns: a property named like one, at any depth.
/// </summary>
/// <remarks>
/// A token in a response body ends up where a script on the page can read it. Tokens go into HttpOnly
/// cookies with <c>AuthCookieExtensions.IssueTokenCookies</c>, and login answers with the user instead; see
/// docs/architecture/authentication-and-permissions.md. The names are exact, so a pagination
/// <c>ContinuationToken</c> or a <c>CsrfToken</c> is not caught.
/// </remarks>
public static class TokensInResponses
{
    private static readonly HashSet<string> TokenNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Token", "AccessToken", "RefreshToken", "IdToken", "Jwt", "BearerToken",
    };

    /// <summary>Every action of every controller in <paramref name="assembly"/>.</summary>
    public static IReadOnlyList<string> Find(Assembly assembly) =>
        Find(assembly.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type) && !type.IsAbstract));

    /// <summary>The actions of these controllers; each finding reads Controller.Action: Type.Property.</summary>
    public static IReadOnlyList<string> Find(IEnumerable<Type> controllers)
    {
        var findings = new List<string>();
        foreach (var controller in controllers)
        foreach (var action in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            var visited = new HashSet<Type>();
            Walk(Unwrap(action.ReturnType), $"{controller.Name}.{action.Name}", visited, findings, depth: 0);
        }

        return findings;
    }

    private static void Walk(Type? type, string where, HashSet<Type> visited, List<string> findings, int depth)
    {
        if (type is null || depth > 6 || IsLeaf(type) || !visited.Add(type))
            return;

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (TokenNames.Contains(property.Name))
                findings.Add($"{where}: {type.Name}.{property.Name}");

            Walk(Unwrap(property.PropertyType), where, visited, findings, depth + 1);
        }
    }

    /// <summary>What a declared type carries: the T of a task, an action result, a collection or a page.</summary>
    private static Type? Unwrap(Type type)
    {
        while (true)
        {
            if (type.IsArray)
            {
                type = type.GetElementType()!;
                continue;
            }

            if (type.IsGenericType && type != typeof(string))
            {
                // Task<T>, ValueTask<T>, ActionResult<T>, IEnumerable<T>, PagedResult<T>: the single type
                // argument is what reaches the body. A dictionary is walked by its value.
                var arguments = type.GetGenericArguments();
                type = arguments[^1];
                continue;
            }

            return type == typeof(Task) || type == typeof(ValueTask) || type == typeof(void) ? null : type;
        }
    }

    private static bool IsLeaf(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
        type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) ||
        type == typeof(DateOnly) || type == typeof(TimeSpan) || type == typeof(object) ||
        typeof(IActionResult).IsAssignableFrom(type) || type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true;
}

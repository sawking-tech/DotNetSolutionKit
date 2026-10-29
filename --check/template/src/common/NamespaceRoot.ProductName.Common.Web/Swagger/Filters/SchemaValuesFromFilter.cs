using System.Collections;
using System.Reflection;
using System.Text.Json;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using NamespaceRoot.ProductName.Common.Contracts.Schema;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NamespaceRoot.ProductName.Common.Web.Swagger.Filters;

/// <summary>
/// Fills in the accepted values of a property marked with <see cref="SchemaValuesFromAttribute"/>,
/// reading them from the source the attribute names.
/// </summary>
/// <remarks>
/// The property stays a string - this only adds the <c>enum</c> list to its schema, so the wire
/// format and the model type are untouched and no client has to change how it sends the value. What
/// changes is that the document now states the values instead of describing them in prose, and it
/// states them from the same place the code checks against, so the two cannot fall out of step.
/// </remarks>
public sealed class SchemaValuesFromFilter : ISchemaFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties is null || schema.Properties.Count == 0) return;

        foreach (var property in context.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attribute = property.GetCustomAttribute<SchemaValuesFromAttribute>();
            if (attribute is null) continue;

            var values = ReadValues(attribute);
            if (values.Count == 0) continue;

            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            if (!schema.Properties.TryGetValue(name, out var propertySchema)) continue;

            propertySchema.Enum = values.Select(v => (IOpenApiAny)new OpenApiString(v)).ToList();
        }
    }

    /// <summary>
    /// Reads the named static member and flattens it to strings. A dictionary contributes its keys -
    /// a lookup table from our value to a provider's is the usual shape, and our side is the one the
    /// caller sends.
    /// </summary>
    private static IReadOnlyList<string> ReadValues(SchemaValuesFromAttribute attribute)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        object? raw = attribute.Source.GetField(attribute.MemberName, flags)?.GetValue(null)
                      ?? attribute.Source.GetProperty(attribute.MemberName, flags)?.GetValue(null);

        return raw switch
        {
            IDictionary dictionary => dictionary.Keys.Cast<object>().Select(k => k.ToString()!).ToList(),
            IEnumerable<string> strings => strings.ToList(),
            _ => [],
        };
    }
}

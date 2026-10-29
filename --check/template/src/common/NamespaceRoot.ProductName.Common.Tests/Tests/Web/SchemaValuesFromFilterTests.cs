using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using NamespaceRoot.ProductName.Common.Contracts.Schema;
using NamespaceRoot.ProductName.Common.Web.Swagger.Filters;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Web;

/// <summary>
/// The schema lists the values a string property accepts, read from the same member the code checks
/// against.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class SchemaValuesFromFilterTests
{
    private static class Statuses
    {
        public static readonly string[] All = ["active", "suspended"];

        public static IReadOnlyDictionary<string, int> ToProvider { get; } =
            new Dictionary<string, int> { ["daily"] = 1, ["monthly"] = 30 };
    }

    private sealed class Model
    {
        [SchemaValuesFrom(typeof(Statuses), nameof(Statuses.All))]
        public string Status { get; init; } = "";

        [SchemaValuesFrom(typeof(Statuses), nameof(Statuses.ToProvider))]
        public string Period { get; init; } = "";

        [SchemaValuesFrom(typeof(Statuses), "Missing")]
        public string Other { get; init; } = "";
    }

    private static OpenApiSchema Apply()
    {
        var schema = new OpenApiSchema
        {
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["status"] = new() { Type = "string" },
                ["period"] = new() { Type = "string" },
                ["other"] = new() { Type = "string" },
            },
        };
        new SchemaValuesFromFilter().Apply(schema, new SchemaFilterContext(typeof(Model), null!, new SchemaRepository()));
        return schema;
    }

    private static string[] Values(OpenApiSchema schema, string property) =>
        schema.Properties[property].Enum.Select(v => ((OpenApiString)v).Value).ToArray();

    [Test(Description = "A list of strings becomes the property's accepted values")]
    public void Should_ListTheValues_FromAStringList() =>
        Values(Apply(), "status").ShouldBe(["active", "suspended"]);

    [Test(Description = "A dictionary contributes its keys, the side the caller sends")]
    public void Should_ListTheKeys_FromADictionary() =>
        Values(Apply(), "period").ShouldBe(["daily", "monthly"]);

    [Test(Description = "A member that does not exist adds nothing rather than failing the document")]
    public void Should_LeaveThePropertyAlone_When_TheMemberIsMissing() =>
        Apply().Properties["other"].Enum.ShouldBeEmpty();
}

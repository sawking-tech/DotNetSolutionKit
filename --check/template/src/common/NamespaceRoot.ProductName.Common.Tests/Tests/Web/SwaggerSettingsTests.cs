using System.ComponentModel.DataAnnotations;
using Microsoft.OpenApi.Models;
using NamespaceRoot.ProductName.Common.Web.Swagger;
using NamespaceRoot.ProductName.Common.Web.Swagger.Filters;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Web;

/// <summary>
/// Swagger:PublicServers and Swagger:ScrubPatterns: checked at startup, and applied to the documents.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class SwaggerSettingsTests
{
    private static List<ValidationResult> Validate(SwaggerSettings settings)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(settings, new ValidationContext(settings), results, validateAllProperties: true);
        return results;
    }

    [Test(Description = "A server is an absolute URL: a relative one leaves the document without a base it can resolve")]
    public void A_server_that_is_not_an_absolute_url_is_refused()
    {
        Validate(new SwaggerSettings { PublicServers = [new SwaggerServer { Url = "https://api.example.com" }] }).ShouldBeEmpty();

        Validate(new SwaggerSettings { PublicServers = [new SwaggerServer { Url = "api.example.com/v1" }] })
            .ShouldHaveSingleItem().ErrorMessage!.ShouldContain("absolute URL");
    }

    [Test]
    public void A_scrub_pattern_that_is_not_a_regular_expression_is_refused()
    {
        Validate(new SwaggerSettings { ScrubPatterns = [@"\bPROJ-\d+\b"] }).ShouldBeEmpty();

        Validate(new SwaggerSettings { ScrubPatterns = ["(unclosed"] })
            .ShouldHaveSingleItem().ErrorMessage!.ShouldContain("not a regular expression");
    }

    [Test(Description = "A tracker id in a description is removed, with the space and the brackets around it")]
    public void The_scrub_patterns_are_removed_from_the_descriptions()
    {
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo { Description = "Orders of a customer (PROJ-12)." },
            Paths = new OpenApiPaths
            {
                ["/api/v1/orders"] = new OpenApiPathItem
                {
                    Operations = { [OperationType.Get] = new OpenApiOperation { Summary = "Lists orders, see PROJ-7 ." } }
                }
            },
            Components = new OpenApiComponents
            {
                Schemas = { ["Order"] = new OpenApiSchema { Description = "An order PROJ-3" } }
            }
        };
        var filter = new ScrubDescriptionsFilter(new SwaggerSettings { ScrubPatterns = [@"\(?\bPROJ-\d+\b\)?", @"\bsee\b"] });

        filter.Apply(document, null!);

        document.Info.Description.ShouldBe("Orders of a customer.");
        document.Paths["/api/v1/orders"].Operations[OperationType.Get].Summary.ShouldBe("Lists orders,.");
        document.Components.Schemas["Order"].Description.ShouldBe("An order");
    }

    [Test]
    public void Without_scrub_patterns_the_document_is_left_as_it_is()
    {
        var document = new OpenApiDocument { Info = new OpenApiInfo { Description = "Orders (PROJ-12)  ." } };

        new ScrubDescriptionsFilter(new SwaggerSettings()).Apply(document, null!);

        document.Info.Description.ShouldBe("Orders (PROJ-12)  .");
    }
}

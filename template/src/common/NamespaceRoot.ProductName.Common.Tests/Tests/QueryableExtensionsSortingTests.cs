using NamespaceRoot.ProductName.Common.Application.Extensions;
using NamespaceRoot.ProductName.Common.Domain.Querying;
using Shouldly;

// The assertion library ships a SortDirection of its own, and the global usings bring it here.
using SortDirection = NamespaceRoot.ProductName.Common.Domain.Querying.SortDirection;

namespace NamespaceRoot.ProductName.Common.Tests.Tests;

/// <summary>
/// Guards case-insensitive text ordering in the shared <see cref="QueryableExtensions.ApplySorting{T}"/>
/// helper: without it, the database collation groups every uppercase-initial name first.
/// </summary>
[TestFixture]
public class QueryableExtensionsSortingTests
{
    private sealed record Row(string Name, int Position);

    private static readonly IReadOnlyDictionary<string, string> Mapping =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "name", "Name" },
            { "position", "Position" },
        };

    private sealed class SortRequest : ISortableRequest
    {
        public string? SortBy { get; init; }
        public SortDirection SortDir { get; init; }
    }

    [Test]
    public void Text_sort_is_case_insensitive_so_names_interleave()
    {
        var rows = new[]
        {
            new Row("Zebra", 0), new Row("apple", 1), new Row("Banana", 2), new Row("cherry", 3),
        }.AsQueryable();

        var sorted = rows
            .ApplySorting(new SortRequest { SortBy = "name" }, Mapping, defaultSort: "Name")
            .Select(r => r.Name)
            .ToList();

        sorted.ShouldBe(new[] { "apple", "Banana", "cherry", "Zebra" });
    }

    [Test]
    public void Non_text_field_keeps_raw_ordering()
    {
        var rows = new[] { new Row("a", 3), new Row("b", 1), new Row("c", 2) }.AsQueryable();

        var sorted = rows
            .ApplySorting(new SortRequest { SortBy = "position" }, Mapping, defaultSort: "Name")
            .Select(r => r.Position)
            .ToList();

        sorted.ShouldBe(new[] { 1, 2, 3 });
    }

    [Test]
    public void Group_expression_leads_the_requested_column()
    {
        var rows = new[]
        {
            new Row("apple", 1), new Row("Zebra", 0), new Row("Banana", 1), new Row("cherry", 0),
        }.AsQueryable();

        var sorted = rows
            .ApplySorting(new SortRequest { SortBy = "name" }, Mapping, defaultSort: "Name",
                groupFirst: "Position")
            .Select(r => r.Name)
            .ToList();

        // Position groups first, the requested name column orders rows inside each group.
        sorted.ShouldBe(new[] { "cherry", "Zebra", "apple", "Banana" });
    }

    [Test]
    public void Group_expression_also_leads_the_default_sort()
    {
        var rows = new[] { new Row("apple", 1), new Row("Zebra", 0) }.AsQueryable();

        var sorted = rows
            .ApplySorting(request: null, Mapping, defaultSort: "Name", groupFirst: "Position")
            .Select(r => r.Name)
            .ToList();

        sorted.ShouldBe(new[] { "Zebra", "apple" });
    }

    [Test]
    public void Default_text_sort_is_also_case_insensitive()
    {
        var rows = new[] { new Row("Zebra", 0), new Row("apple", 1) }.AsQueryable();

        var sorted = rows
            .ApplySorting(request: null, Mapping, defaultSort: "Name")
            .Select(r => r.Name)
            .ToList();

        sorted.ShouldBe(new[] { "apple", "Zebra" });
    }

    // A defaultSort that already carries its own direction must be used verbatim, not have
    // " ascending" appended: that builds the invalid Dynamic LINQ "Position descending ascending".
    [Test]
    public void Default_sort_with_explicit_direction_is_used_verbatim()
    {
        var rows = new[] { new Row("a", 1), new Row("b", 3), new Row("c", 2) }.AsQueryable();

        var sorted = rows
            .ApplySorting(request: null, Mapping, defaultSort: "Position descending")
            .Select(r => r.Position)
            .ToList();

        sorted.ShouldBe(new[] { 3, 2, 1 });
    }
}

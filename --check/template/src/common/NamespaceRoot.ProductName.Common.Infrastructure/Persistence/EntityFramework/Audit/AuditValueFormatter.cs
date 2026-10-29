using System.Collections;
using System.Globalization;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Audit;

/// <summary>
/// Renders a property value the way the journal stores it.
/// </summary>
/// <remarks>
/// Shared by both capture paths — the interceptor and the set-based capture — so a diff reads the
/// same however the change was written. Values are text because the journal is read by people, and
/// a stable string survives type changes in the emitting entity that a structurally serialised
/// value would not.
/// </remarks>
internal static class AuditValueFormatter
{
    public static string? Stringify(object? value) => value switch
    {
        null => null,
        string s => s,

        // ISO 8601 rather than the invariant culture's MM/dd/yyyy, which reads as a different date
        // to most of the people this journal is for, and does not sort as text.
        DateTimeOffset offset => offset.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),

        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),

        // Collections render their contents, not their type name. A materialised tree path is an int[],
        // and left to ToString() a node being moved recorded "System.Int32[]" moving to
        // "System.Int32[]" — the one change the column exists to show, reported as no change at all.
        IEnumerable items => "[" + string.Join(", ", items.Cast<object?>().Select(Stringify)) + "]",

        _ => value.ToString(),
    };
}

/// <summary>
/// Columns every entity carries, which change on any write and describe no operator intent.
/// </summary>
/// <remarks>
/// Filtering them centrally spares every entity an <c>[AuditIgnore]</c>. Concurrency tokens and
/// primary keys are excluded too, but read from EF metadata rather than by name — they are spelled
/// differently per entity, and the key is already reported as the entry's identifier.
/// </remarks>
internal static class AuditColumnPolicy
{
    private static readonly HashSet<string> AlwaysIgnored =
        new(StringComparer.Ordinal) { "CreatedAt", "UpdatedAt" };

    public static bool IsAlwaysIgnored(string propertyName) => AlwaysIgnored.Contains(propertyName);
}

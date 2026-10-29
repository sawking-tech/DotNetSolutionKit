using System.Globalization;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Utility;

namespace NamespaceRoot.ProductName.Common.Infrastructure.ClickHouse;

/// <summary>
/// The driver hands values back weakly typed. One coercion for every reader, so two endpoints do not
/// end up disagreeing about what an empty column means.
/// </summary>
public static class ClickHouseValues
{
    /// <summary>
    /// Binds the <c>LIMIT {p_page_size:UInt32} OFFSET {p_offset:UInt64}</c> placeholders of a paged query.
    /// </summary>
    public static void ApplyPagination(ClickHouseCommand command, int page, int pageSize)
    {
        command.AddParameter("p_page_size", "UInt32", (uint)pageSize);
        // Widened before multiplying: page * pageSize overflows int well within the range a caller can
        // legally ask for.
        command.AddParameter("p_offset", "UInt64", (ulong)((page - 1) * (long)pageSize));
    }

    public static long ToLong(object? value) => value switch
    {
        null or DBNull => 0L,
        long l => l,
        var v => Convert.ToInt64(v, CultureInfo.InvariantCulture),
    };

    public static int ToInt(object? value) => value switch
    {
        null or DBNull => 0,
        int i => i,
        var v => Convert.ToInt32(v, CultureInfo.InvariantCulture),
    };

    public static bool ToBool(object? value) => value switch
    {
        null or DBNull => false,
        bool b => b,
        // A flag column comes back as UInt8: 1 is true, anything else false.
        var v => Convert.ToInt32(v, CultureInfo.InvariantCulture) == 1,
    };

    public static DateTimeOffset ToDateTimeOffset(object? value) => value switch
    {
        null or DBNull => DateTimeOffset.MinValue,
        DateTime dt => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)),
        DateTimeOffset dto => dto,
        var v => new DateTimeOffset(DateTime.SpecifyKind(Convert.ToDateTime(v, CultureInfo.InvariantCulture), DateTimeKind.Utc)),
    };

    public static string? NullableString(object? value) =>
        value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
}

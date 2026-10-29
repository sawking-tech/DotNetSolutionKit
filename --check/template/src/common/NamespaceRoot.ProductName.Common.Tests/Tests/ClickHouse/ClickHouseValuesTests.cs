using NamespaceRoot.ProductName.Common.Infrastructure.ClickHouse;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.ClickHouse;

/// <summary>
/// What a weakly typed driver value becomes, and what the schema check compares, without a server.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class ClickHouseValuesTests
{
    [Test(Description = "An empty column is a zero, false, null or the minimal timestamp, the same for every reader")]
    public void Should_TurnAnEmptyColumnIntoADefault()
    {
        ClickHouseValues.ToLong(DBNull.Value).ShouldBe(0);
        ClickHouseValues.ToInt(null).ShouldBe(0);
        ClickHouseValues.ToBool(DBNull.Value).ShouldBeFalse();
        ClickHouseValues.NullableString(DBNull.Value).ShouldBeNull();
        ClickHouseValues.ToDateTimeOffset(null).ShouldBe(DateTimeOffset.MinValue);
    }

    [Test(Description = "Unsigned and narrower integers convert, and a UInt8 flag of 1 is true")]
    public void Should_ConvertDriverTypes()
    {
        ClickHouseValues.ToLong((ulong)42).ShouldBe(42);
        ClickHouseValues.ToInt((byte)7).ShouldBe(7);
        ClickHouseValues.ToBool((byte)1).ShouldBeTrue();
        ClickHouseValues.ToBool((byte)0).ShouldBeFalse();
    }

    [Test(Description = "A DateTime from the server is read as UTC")]
    public void Should_ReadDateTimeAsUtc() =>
        ClickHouseValues.ToDateTimeOffset(new DateTime(2026, 1, 19, 10, 0, 0, DateTimeKind.Unspecified)).Offset.ShouldBe(TimeSpan.Zero);

    [Test(Description = "Missing columns are reported in the order the insert lists them")]
    public void Should_ReportMissingColumnsInInsertOrder() =>
        ClickHouseSchemaGuard.FindMissingColumns(["id", "amount", "currency", "at"], new HashSet<string> { "id", "at" })
            .ShouldBe(["amount", "currency"]);

    [TestCase("reports.usage", "reports", "usage", TestName = "A qualified table names its database")]
    [TestCase("usage", "default", "usage", TestName = "An unqualified table is in default")]
    public void Should_SplitTheTableName(string qualified, string database, string table) =>
        ClickHouseSchemaGuard.SplitQualifiedName(qualified).ShouldBe((database, table));
}

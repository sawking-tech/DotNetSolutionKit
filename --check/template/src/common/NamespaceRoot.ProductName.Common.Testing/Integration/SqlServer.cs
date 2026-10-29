namespace NamespaceRoot.ProductName.Common.Tests.Integration;

/// <summary>
/// The SQL Server an integration test runs against, from the <c>TEST_SQLSERVER</c> environment variable.
/// </summary>
/// <remarks>
/// Without the variable the test is skipped with that reason rather than failed, so a plain
/// <c>dotnet test</c> needs no database. CI that runs integration tests sets it to a throwaway container:
/// <c>Server=localhost,1433;User Id=sa;Password=...;TrustServerCertificate=true</c>.
/// </remarks>
public static class SqlServer
{
    public const string Variable = "TEST_SQLSERVER";

    public static string ConnectionString()
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(value))
            TestSkip.Because($"{Variable} is not set: no SQL Server to run integration tests against.");
        return value!;
    }
}

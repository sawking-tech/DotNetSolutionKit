namespace NamespaceRoot.ProductName.Common.Tests.Integration;

/// <summary>
/// The PostgreSQL an integration test runs against, from the <c>TEST_POSTGRES</c> environment variable.
/// </summary>
/// <remarks>
/// Without the variable the test is skipped with that reason rather than failed, so a plain
/// <c>dotnet test</c> needs no database. CI that runs integration tests sets it, for example to a
/// throwaway container: <c>Host=localhost;Database=tests;Username=postgres;Password=...</c>.
/// </remarks>
public static class Postgres
{
    public const string Variable = "TEST_POSTGRES";

    public static string ConnectionString()
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(value))
            TestSkip.Because($"{Variable} is not set: no PostgreSQL to run integration tests against.");
        return value!;
    }
}

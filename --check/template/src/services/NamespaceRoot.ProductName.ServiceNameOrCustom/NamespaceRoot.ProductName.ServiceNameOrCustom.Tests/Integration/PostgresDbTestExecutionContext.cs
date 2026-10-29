using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Tests;
using NamespaceRoot.ProductName.Common.Tests.Integration;
using NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.EntityFramework;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Tests.Integration;

/// <summary>
/// A test on this service's real PostgreSQL schema: a database of its own, cloned from one migrated
/// once per test run, and dropped after the test. For what the in-memory provider cannot show:
/// transactions, constraints, raw SQL, the migrations themselves. Needs <c>TEST_POSTGRES</c>; the test is
/// skipped without it.
/// </summary>
/// <example>
/// <code>
/// [Test, Integration]
/// public async Task Should_RejectADuplicateNumber()
/// {
///     await using var ctx = await PostgresDbTestExecutionContext&lt;OrderService&gt;.CreateAsync();
///     ...
/// }
/// </code>
/// </example>
internal sealed class PostgresDbTestExecutionContext<TService>
    : PostgresIntegrationTestBase<TService, ServiceIdentifierDbContext>
    where TService : class
{
    private PostgresDbTestExecutionContext(string adminConnStr, string testDbName, string testConnStr,
        Action<IServiceCollection>? configure)
        : base(adminConnStr, testDbName, testConnStr, configure) { }

    public static Task<PostgresDbTestExecutionContext<TService>> CreateAsync(
        Action<IServiceCollection>? configure = null)
        => CreateCoreAsync<PostgresDbTestExecutionContext<TService>>(
            Postgres.ConnectionString(),
            "servicenameorcustom_test",
            (admin, dbName, connStr, cfg) => new PostgresDbTestExecutionContext<TService>(admin, dbName, connStr, cfg),
            configure);
}

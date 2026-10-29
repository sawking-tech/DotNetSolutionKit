using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence;

namespace NamespaceRoot.ProductName.Common.Tests;

/// <summary>
/// The provider of the solution's database, for a context a unit test builds without connecting to it:
/// the model and the SQL it would send are the provider's own.
/// </summary>
public static class TestDatabase
{
    public static DbContextOptionsBuilder<TContext> UseSolutionDatabase<TContext>(
        this DbContextOptionsBuilder<TContext> builder, string connectionString)
        where TContext : DbContext
    {
        builder.UseDatabase(connectionString);
        return builder;
    }
}

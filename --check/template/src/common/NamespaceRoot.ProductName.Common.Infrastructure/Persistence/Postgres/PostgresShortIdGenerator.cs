using System.Data;
using Microsoft.EntityFrameworkCore;
using NamespaceRoot.ProductName.Common.Application.Persistence;
using Npgsql;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.Postgres;

/// <summary>
/// <see cref="IShortIdGenerator"/> on a PostgreSQL sequence, read through the connection of
/// <typeparamref name="TContext"/> and outside the change tracker.
/// </summary>
/// <remarks>
/// The sequence name goes in as a parameter cast to <c>regclass</c>, never into the text of the query,
/// so a name taken from anywhere cannot change the statement.
/// </remarks>
public sealed class PostgresShortIdGenerator<TContext>(TContext context) : IShortIdGenerator
    where TContext : DbContext
{
    public async Task<long> GetNextAsync(string sequenceName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceName);

        var connection = context.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
            await context.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT nextval(@sequence::regclass)";
            command.Parameters.Add(new NpgsqlParameter("sequence", sequenceName));
            return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            if (openedHere)
                await context.Database.CloseConnectionAsync();
        }
    }
}

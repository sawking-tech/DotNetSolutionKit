using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using NamespaceRoot.ProductName.Common.Application.Persistence;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.SqlServer;

/// <summary>
/// <see cref="IShortIdGenerator"/> on a SQL Server sequence, read through the connection of
/// <typeparamref name="TContext"/> and outside the change tracker.
/// </summary>
/// <remarks>
/// <c>NEXT VALUE FOR</c> takes no parameter, so the number comes from <c>sp_sequence_get_range</c>, which
/// takes the sequence's name as one: a name taken from anywhere cannot change the statement.
/// </remarks>
public sealed class SqlServerShortIdGenerator<TContext>(TContext context) : IShortIdGenerator
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
            command.CommandText = "sp_sequence_get_range";
            command.CommandType = CommandType.StoredProcedure;
            command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(new SqlParameter("@sequence_name", sequenceName));
            command.Parameters.Add(new SqlParameter("@range_size", 1L));
            var first = new SqlParameter("@range_first_value", SqlDbType.Variant) { Direction = ParameterDirection.Output };
            command.Parameters.Add(first);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return Convert.ToInt64(first.Value);
        }
        finally
        {
            if (openedHere)
                await context.Database.CloseConnectionAsync();
        }
    }
}

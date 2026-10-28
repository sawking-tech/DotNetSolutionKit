// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using System.Data;
using Microsoft.Data.SqlClient;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.SqlServer;

/// <summary>
/// The schema guard on SQL Server: a schema is either empty or owned by the service that claims it, by the
/// same rules as on PostgreSQL.
/// </summary>
public static class SqlServerSchemaGuard
{
    // 4060: the database cannot be opened yet; -2: a timeout; 53 and 40: no server to talk to yet.
    private static readonly int[] StartingUpNumbers = [4060, -2, 53, 40];
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private const int MaxRetries = 30;

    /// <summary>
    /// Ensures that <paramref name="schemaName"/> is either empty or owned by <paramref name="serviceName"/>,
    /// creating and claiming it when it is new. Retries while the server is still starting.
    /// </summary>
    /// <exception cref="InvalidOperationException">The schema is owned by another service.</exception>
    public static void EnsureExclusiveSchema(string connectionString, string schemaName, string serviceName)
    {
        EnsureDatabase(connectionString);
        using var connection = OpenWithRetry(connectionString);
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);
        try
        {
            // Replicas of one service start together: one lock per schema, released with the transaction,
            // makes the second replica wait and then find everything in place.
            using (var command = new SqlCommand("sp_getapplock", connection, transaction) { CommandType = CommandType.StoredProcedure })
            {
                command.Parameters.Add(new SqlParameter("@Resource", $"schema-guard:{schemaName}"));
                command.Parameters.Add(new SqlParameter("@LockMode", "Exclusive"));
                command.Parameters.Add(new SqlParameter("@LockOwner", "Transaction"));
                command.Parameters.Add(new SqlParameter("@LockTimeout", -1));
                command.ExecuteNonQuery();
            }

            var schema = Quote(schemaName);

            // CREATE SCHEMA must be the only statement of its batch, so it runs through EXEC.
            Execute(connection, transaction,
                "IF SCHEMA_ID(@schema) IS NULL EXEC('CREATE SCHEMA ' + @quoted)",
                ("@schema", schemaName), ("@quoted", schema));

            Execute(connection, transaction,
                $"IF OBJECT_ID(@table, 'U') IS NULL CREATE TABLE {schema}.[_service_metadata] " +
                "([key] NVARCHAR(200) NOT NULL PRIMARY KEY, [value] NVARCHAR(MAX) NULL)",
                ("@table", $"{schema}.[_service_metadata]"));

            string? currentOwner;
            using (var command = new SqlCommand(
                       $"SELECT [value] FROM {schema}.[_service_metadata] WITH (UPDLOCK, HOLDLOCK) WHERE [key] = 'owner'",
                       connection, transaction))
            {
                currentOwner = command.ExecuteScalar() as string;
            }

            if (currentOwner == null)
            {
                Execute(connection, transaction,
                    $"INSERT INTO {schema}.[_service_metadata] ([key], [value]) VALUES ('owner', @name)",
                    ("@name", serviceName));
            }
            else if (!string.Equals(currentOwner, serviceName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"CRITICAL: SQL Server schema '{schemaName}' is already owned by '{currentOwner}'. " +
                    $"Service '{serviceName}' is not allowed to use it to avoid data contamination.");
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Creates the database of <paramref name="connectionString"/> when there is none. PostgreSQL's container
    /// creates its database from POSTGRES_DB; SQL Server's has only master, and the guard connects before the
    /// migrations would create it. An existing database is left as it is, so a database a DBA made first
    /// needs no right to create one.
    /// </summary>
    private static void EnsureDatabase(string connectionString)
    {
        var target = new SqlConnectionStringBuilder(connectionString);
        if (string.IsNullOrEmpty(target.InitialCatalog))
            return;

        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;
        using var connection = OpenWithRetry(master);
        using var command = new SqlCommand(
            "IF DB_ID(@name) IS NULL EXEC('CREATE DATABASE ' + @quoted)", connection);
        command.Parameters.Add(new SqlParameter("@name", target.InitialCatalog));
        command.Parameters.Add(new SqlParameter("@quoted", Quote(target.InitialCatalog)));
        command.ExecuteNonQuery();
    }

    // A schema name goes into the text of DDL, so it is quoted the way SQL Server quotes an identifier.
    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";

    private static void Execute(SqlConnection connection, SqlTransaction transaction, string sql,
        params (string Name, string Value)[] parameters)
    {
        using var command = new SqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters)
            command.Parameters.Add(new SqlParameter(name, value));
        command.ExecuteNonQuery();
    }

    private static SqlConnection OpenWithRetry(string connectionString)
    {
        for (var attempt = 0; ; attempt++)
        {
            var connection = new SqlConnection(connectionString);
            try
            {
                connection.Open();
                return connection;
            }
            catch (SqlException ex) when (StartingUpNumbers.Contains(ex.Number) && attempt < MaxRetries - 1)
            {
                connection.Dispose();
                Console.Error.WriteLine(
                    $"[SqlServerSchemaGuard] Database is not accepting connections yet ({ex.Message}), retrying in " +
                    $"{RetryDelay.TotalSeconds}s (attempt {attempt + 1}/{MaxRetries})...");
                Thread.Sleep(RetryDelay);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
        }
    }
}

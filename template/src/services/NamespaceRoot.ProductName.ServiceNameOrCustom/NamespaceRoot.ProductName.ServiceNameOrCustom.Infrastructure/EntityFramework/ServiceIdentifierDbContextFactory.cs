using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.EntityFramework;

[UsedImplicitly]
public class ServiceIdentifierDbContextFactory : IDesignTimeDbContextFactory<ServiceIdentifierDbContext>
{
    private static DbContextOptions<ServiceIdentifierDbContext> GetOptions(string connectionString)
    {
        var options = new DbContextOptionsBuilder<ServiceIdentifierDbContext>();
        options.UseDatabase(connectionString, ServiceIdentifierDbContext.DefaultSchemaName);
        return options.Options;
    }

    private static string GetConnectionString()
    {
        // First try environment variables (works everywhere)
        var envConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (!string.IsNullOrEmpty(envConnectionString))
        {
            Console.WriteLine("Using connection string from environment variables");
            return envConnectionString;
        }

        var currentDir = Directory.GetCurrentDirectory();
        Console.WriteLine($"Current directory: {currentDir}");

        // If we're in Infrastructure, go up one level to API project
        var apiProjectPath = Path.Combine(currentDir, "..", "NamespaceRoot.ProductName.ServiceNameOrCustom.API");
        var apiProjectFullPath = Path.GetFullPath(apiProjectPath);

        Console.WriteLine($"Looking for configs in: {apiProjectFullPath}");

        if (!Directory.Exists(apiProjectFullPath))
        {
            throw new Exception($"API project directory not found: {apiProjectFullPath}");
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiProjectFullPath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddJsonFile("appsettings.Secrets.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrEmpty(connectionString))
        {
            // Adding a migration compares the model with the snapshot and never opens a connection,
            // so a freshly generated service can get its first migration before any database exists.
            // Commands that do connect, such as database update and migrations list, fail on this
            // placeholder with a connection error naming localhost.
            Console.WriteLine(
                "Connection string 'DefaultConnection' not found: using a placeholder. " +
                "Enough to add a migration; set ConnectionStrings__DefaultConnection to touch a database.");
            return DatabaseProvider.DesignTimeConnectionString;
        }

        Console.WriteLine("Successfully found connection string");
        return connectionString;
    }

    public ServiceIdentifierDbContext CreateDbContext(string[] args)
    {
        var connectionString = GetConnectionString();

        // Name the target database without the credentials: this output lands in terminals and CI logs.
        Console.WriteLine($"Database: {DatabaseProvider.DescribeTarget(connectionString)}");

        return new ServiceIdentifierDbContext(GetOptions(connectionString));
    }
}

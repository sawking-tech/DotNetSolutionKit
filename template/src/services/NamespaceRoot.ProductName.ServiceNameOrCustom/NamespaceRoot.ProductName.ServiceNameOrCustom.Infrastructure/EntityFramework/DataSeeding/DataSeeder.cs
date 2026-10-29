using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.EntityFramework.DataSeeding.Seeders;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.EntityFramework.DataSeeding;

public class DataSeeder
{
    private readonly ILogger<DataSeeder> _logger;
    private readonly bool _isProduction;

    public DataSeeder(
        ILogger<DataSeeder> logger,
        IHostEnvironment environment)
    {
        _logger = logger;
        _isProduction = environment.IsProduction();
    }

    public Task SeedAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting data seeding...");

        // Seeders run here through RunSeederAsync; the method becomes async with the first of them.

        _logger.LogInformation("Data seeding completed");
        return Task.CompletedTask;
    }

    private async Task RunSeederAsync(
        DataSeederBase seeder,
        string seederName,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Running seeder: {SeederName}", seederName);

        try
        {
            await seeder.SeedAsync(cancellationToken);
            _logger.LogDebug("Completed seeder: {SeederName}", seederName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run seeder: {SeederName}", seederName);
            throw;
        }
    }
}

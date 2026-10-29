using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NamespaceRoot.ProductName.Common.Application.Configuration;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Configuration;

/// <summary>
/// A reloadable setting takes each new valid value of its section and keeps the last valid one when a new
/// value fails its annotations.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class ReloadableTests
{
    private sealed class Limits
    {
        [System.ComponentModel.DataAnnotations.Range(1, 1000)]
        public int PageSize { get; set; } = 50;
    }

    private static (IReloadable<Limits> Limits, MemoryConfigurationProvider Source, IConfigurationRoot Root) Build()
    {
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Limits:PageSize"] = "50" })
            .Build();
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(root)
            .AddLogging()
            .AddReloadableOptions<Limits>("Limits");
        var limits = services.BuildServiceProvider().GetRequiredService<IReloadable<Limits>>();
        return (limits, root.Providers.OfType<MemoryConfigurationProvider>().Single(), root);
    }

    [Test]
    public void A_new_valid_value_is_taken()
    {
        var (limits, source, root) = Build();

        source.Set("Limits:PageSize", "200");
        root.Reload();

        limits.Current.PageSize.ShouldBe(200);
    }

    [Test]
    public void A_new_value_that_fails_validation_is_not_taken()
    {
        var (limits, source, root) = Build();

        source.Set("Limits:PageSize", "0");
        root.Reload();

        limits.Current.PageSize.ShouldBe(50);
    }

    [Test]
    public void A_value_that_fails_validation_at_start_stops_the_service()
    {
        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Limits:PageSize"] = "0" })
            .Build();
        var provider = new ServiceCollection().AddSingleton<IConfiguration>(root).AddLogging()
            .AddReloadableOptions<Limits>("Limits").BuildServiceProvider();

        Should.Throw<ValidationException>(() => provider.GetRequiredService<IReloadable<Limits>>());
    }
}

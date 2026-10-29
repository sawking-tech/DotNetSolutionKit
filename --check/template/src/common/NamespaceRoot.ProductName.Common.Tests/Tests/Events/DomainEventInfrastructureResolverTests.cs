using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Events;

[TestFixture]
[TestOf(typeof(DomainEventInfrastructureResolver))]
[Parallelizable(ParallelScope.All)]
public class DomainEventInfrastructureResolverTests
{
    /// <summary>
    /// Reproduces the seeding crash: DbContext is used within a scope,
    /// but DomainEventScopeContext is NOT set (simulates startup data seeding).
    /// Previously caused InvalidOperationException when accessing root provider.
    /// </summary>
    [Test]
    [Description("TryResolve must return false gracefully when no ambient scope is set (seeding/migration context)")]
    public async Task TryResolve_ReturnsFalse_WhenNoAmbientScopeSet()
    {
        await using var ctx = CreateTestContext();

        await ctx.ExecuteAsync<TestDbContext>(db =>
        {
            // DomainEventScopeContext is not set - simulates seeding
            var result = DomainEventInfrastructureResolver.TryResolve(db, out var storage, out var dispatcher);

            result.ShouldBeFalse();
            storage.ShouldBeNull();
            dispatcher.ShouldBeNull();
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Simulates a MassTransit consumer: DomainEventScopeFilter sets the ambient scope
    /// before handling a message, allowing the singleton interceptor to find scoped services.
    /// </summary>
    [Test]
    [Description("TryResolve must return true and resolve services when DomainEventScopeContext is set (MassTransit/Hangfire context)")]
    public async Task TryResolve_ReturnsTrue_WhenAmbientScopeContextIsSet()
    {
        await using var ctx = CreateTestContext();

        IServiceProvider? scopeSp = null;
        await ctx.ExecuteAsync<TestDbContext>(
            db =>
            {
                using (DomainEventScopeContext.Use(scopeSp!))
                {
                    var result = DomainEventInfrastructureResolver.TryResolve(db, out var storage, out var dispatcher);

                    result.ShouldBeTrue();
                    storage.ShouldNotBeNull();
                    dispatcher.ShouldNotBeNull();
                }
                return Task.CompletedTask;
            },
            configure: sp => scopeSp = sp);
    }

    [Test]
    [Description("TryResolve must return false once the ambient scope is disposed - no stale references")]
    public async Task TryResolve_ReturnsFalse_AfterAmbientScopeDisposed()
    {
        using var ctx = CreateTestContext();

        IServiceProvider? scopeSp = null;
        await ctx.ExecuteAsync<TestDbContext>(
            db =>
            {
                var ambientScope = DomainEventScopeContext.Use(scopeSp!);
                ambientScope.Dispose();

                var result = DomainEventInfrastructureResolver.TryResolve(db, out _, out _);
                result.ShouldBeFalse();
                return Task.CompletedTask;
            },
            configure: sp => scopeSp = sp);
    }

    // --- Setup ---

    private static ResolverTestContext CreateTestContext() => new();

    private sealed class ResolverTestContext : DbTestExecutionContext<TestDbContext>
    {
        public ResolverTestContext()
        {
            Services.AddDomainEventCore();
            Services.AddDomainEventPersistence();
            Services.AddDbContext<TestDbContext>((sp, options) =>
            {
                options.UseInMemoryDatabase($"ResolverTest_{Guid.NewGuid()}");
                options.ApplyDomainEventInterceptors(sp);
            });
        }
    }

    private sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options);
}

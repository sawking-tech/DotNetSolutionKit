using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;

namespace NamespaceRoot.ProductName.Common.Tests;

public class TestExecutionContext : IDisposable, IAsyncDisposable
{
    // WHY these dictionaries exist:
    // A typical test calls ActAsync() then AssertAsync() — each creates its own DI scope.
    // Plain AddScoped would produce a different instance per scope, so mocks registered
    // in one scope wouldn't be visible in another, and factory-built services would be
    // recreated on every call.
    //
    // The dictionaries give us "singleton-within-test" semantics while keeping the DI
    // lifetime as Scoped (AddScoped(_ => instance / GetOrCreateInstance)).
    // Scoped lifetime is intentional: it prevents DI scope-validation errors when
    // the registered service is injected into other Scoped services (e.g. repositories
    // injected into a service-under-test that also holds a DbContext).
    //
    // DO NOT replace with AddSingleton — that breaks scope validation.
    // DO NOT remove the dictionaries — that breaks cross-scope mock visibility.
    private readonly Dictionary<Type, Func<object>> _instanceFactories = new();
    private readonly Dictionary<Type, object> _cachedInstances = new();

    private ServiceProvider? _serviceProvider;
    private bool _disposed;

    public IServiceCollection Services { get; } = new ServiceCollection();

    private ServiceProvider GetServiceProvider()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TestExecutionContext));

        return _serviceProvider ??= Services.BuildServiceProvider();
    }

    public void Register<TService, TImplementation>(Func<TImplementation>? factory = null)
        where TService : class
        where TImplementation : class, TService
    {
        if (factory != null)
        {
            _instanceFactories[typeof(TService)] = factory;
            _instanceFactories[typeof(TImplementation)] = factory;
            Services.AddScoped<TService>(_ => GetOrCreateInstance<TService>());
            Services.AddScoped<TImplementation>(_ => GetOrCreateInstance<TImplementation>());
        }
        else
        {
            Services.AddScoped<TService, TImplementation>();
            Services.AddScoped<TImplementation>();
        }
    }

    public void Register<TImplementation>(Func<TImplementation>? factory = null) where TImplementation : class
    {
        if (factory != null)
        {
            _instanceFactories[typeof(TImplementation)] = factory;
            Services.AddScoped(_ => GetOrCreateInstance<TImplementation>());
        }
        else
        {
            Services.AddScoped<TImplementation>();
        }
    }

    public void Register<TService, TImplementation>(TImplementation instance)
        where TService : class
        where TImplementation : class, TService
    {
        _cachedInstances[typeof(TService)] = instance;
        _cachedInstances[typeof(TImplementation)] = instance;

        Services.AddScoped<TService>(_ => instance);
        Services.AddScoped<TImplementation>(_ => instance);
    }

    public void Register<TImplementation>(TImplementation instance) where TImplementation : class
    {
        _cachedInstances[typeof(TImplementation)] = instance;
        Services.AddScoped(_ => instance);
    }

    private T GetOrCreateInstance<T>() where T : class
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TestExecutionContext));

        var type = typeof(T);

        if (_cachedInstances.TryGetValue(type, out var instance))
            return (T)instance;

        if (_instanceFactories.TryGetValue(type, out var factory))
            return (T)factory();

        return GetServiceProvider().GetRequiredService<T>();
    }

    public T GetRegistered<T>() where T : class
    {
        return GetOrCreateInstance<T>();
    }

    public async Task<TResult> ExecuteAsync<TService, TResult>(
        Func<TService, Task<TResult>> execute,
        Action<IServiceProvider>? configure = null) where TService : notnull
    {
        var serviceProvider = GetServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();

        configure?.Invoke(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        return await execute(service);
    }

    public async Task ExecuteAsync<TService>(
        Func<TService, Task> execute,
        Action<IServiceProvider>? configure = null) where TService : notnull
    {
        var serviceProvider = GetServiceProvider();
        await using var scope = serviceProvider.CreateAsyncScope();

        configure?.Invoke(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        await execute(service);
    }

    public TResult Execute<TService, TResult>(
        Func<TService, TResult> execute,
        Action<IServiceProvider>? configure = null) where TService : notnull
    {
        var serviceProvider = GetServiceProvider();
        using var scope = serviceProvider.CreateScope();

        configure?.Invoke(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        return execute(service);
    }

    public void Execute<TService>(
        Action<TService> execute,
        Action<IServiceProvider>? configure = null) where TService : notnull
    {
        var serviceProvider = GetServiceProvider();
        using var scope = serviceProvider.CreateScope();

        configure?.Invoke(scope.ServiceProvider);
        var service = scope.ServiceProvider.GetRequiredService<TService>();
        execute(service);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _serviceProvider?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            if (_serviceProvider != null)
                await _serviceProvider.DisposeAsync();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Context with DbContext support and database operations.
/// </summary>
public class DbTestExecutionContext<TDbContext> : TestExecutionContext where TDbContext : DbContext
{
    public async Task ArrangeAsync(params object[] entities) =>
        await ExecuteAsync<TDbContext>(async db => { db.AddRange(entities); await db.SaveChangesAsync(); });

    public virtual async Task EnsureDatabaseCreatedAsync() =>
        await ExecuteAsync<TDbContext>(db => db.Database.EnsureCreatedAsync());

    public virtual async Task EnsureDatabaseDeletedAsync() =>
        await ExecuteAsync<TDbContext>(db => db.Database.EnsureDeletedAsync());

    public Task AssertAsync(Func<TDbContext, Task> assert) => ExecuteAsync(assert);
}

/// <summary>
/// Context with service but without DbContext.
/// </summary>
public class ServiceTestExecutionContext<TService> : TestExecutionContext where TService : class
{
    public ServiceTestExecutionContext() => Services.AddScoped<TService>();

    public Task ActAsync(Func<TService, Task> act, Action<IServiceProvider>? configure = null) =>
        ExecuteAsync(act, configure);

    public Task<TResult> ActAsync<TResult>(Func<TService, Task<TResult>> act, Action<IServiceProvider>? configure = null) =>
        ExecuteAsync(act, configure);

    public TResult Act<TResult>(Func<TService, TResult> act, Action<IServiceProvider>? configure = null) =>
        Execute(act, configure);

    public void Act(Action<TService> act, Action<IServiceProvider>? configure = null) =>
        Execute(act, configure);
}

/// <summary>
/// Context with service and DbContext.
/// </summary>
public class ServiceDbTestExecutionContext<TService, TDbContext> : DbTestExecutionContext<TDbContext>
    where TService : class
    where TDbContext : DbContext
{
    public ServiceDbTestExecutionContext() => Services.AddScoped<TService>();

    // The act publishes its scope as the ambient one, so the singleton EF interceptors find the scoped event
    // storage and dispatcher of this act (DomainEventInfrastructureResolver); without it they skip the
    // events, as there is no HTTP context in a test. The same for every provider.
    public Task ActAsync(Func<TService, Task> act, Action<IServiceProvider>? configure = null)
    {
        IServiceProvider? scopeSp = null;
        return ExecuteAsync<TService>(
            async svc =>
            {
                using (DomainEventScopeContext.Use(scopeSp!))
                    await act(svc);
            },
            configure: sp =>
            {
                scopeSp = sp;
                configure?.Invoke(sp);
            });
    }

    public Task<TResult> ActAsync<TResult>(Func<TService, Task<TResult>> act, Action<IServiceProvider>? configure = null)
    {
        IServiceProvider? scopeSp = null;
        return ExecuteAsync<TService, TResult>(
            async svc =>
            {
                using (DomainEventScopeContext.Use(scopeSp!))
                    return await act(svc);
            },
            configure: sp =>
            {
                scopeSp = sp;
                configure?.Invoke(sp);
            });
    }

    public TResult Act<TResult>(Func<TService, TResult> act, Action<IServiceProvider>? configure = null) =>
        Execute(act, configure);

    public void Act(Action<TService> act, Action<IServiceProvider>? configure = null) =>
        Execute(act, configure);
}

/// <summary>
/// InMemory variant for unit tests: one database per test, and the domain event interceptors applied when
/// the test registers domain events; ActAsync publishes its scope to them, as on every provider.
/// </summary>
public class InMemoryTestExecutionContext<TService, TDbContext> : ServiceDbTestExecutionContext<TService, TDbContext>
    where TService : class
    where TDbContext : DbContext
{
    public InMemoryTestExecutionContext()
    {
        // The name is taken once, for the test: the options are built per scope, and a name taken inside
        // the callback would give every scope a database of its own - an arrange the act cannot see, an
        // act the assert cannot see.
        var database = $"TestDb_{Guid.NewGuid():N}";
        Services.AddDbContext<TDbContext>((sp, options) =>
        {
            options.UseInMemoryDatabase(database);
            options.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            if (sp.GetService<DomainEventPreSaveInterceptor>() != null)
                options.ApplyDomainEventInterceptors(sp);
        });
    }

    public override Task EnsureDatabaseCreatedAsync() => Task.CompletedTask;
    public override Task EnsureDatabaseDeletedAsync() => Task.CompletedTask;
}

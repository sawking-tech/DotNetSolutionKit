using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Application.Execution;
using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Security;

public static class DependencyInjection
{
    private static void AddUserContext(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddScoped<IUserContext>(sp =>
        {
            // Inside a background job or a consumer there are no claims to read, and asking
            // HttpUserContext for a user id throws. Resolution order says what the caller actually
            // is: an HTTP request, then a job or message carrying the actor that triggered it, then
            // the system.
            if (sp.GetRequiredService<IHttpContextAccessor>() is { HttpContext: not null } http)
                return new HttpUserContext(http);

            return JobActorContext.Actor ?? SystemUserContext.Instance;
        });
    }

    public static IServiceCollection AddExecutionContext(this IServiceCollection services)
    {
        services.AddUserContext();
        services.AddScoped<IDomainExecutionContext>(sp =>
        {
            var actor = sp.GetRequiredService<IUserContext>();
            var timeProvider = sp.GetRequiredService<TimeProvider>();

            return new ApplicationExecutionContext(actor, timeProvider);
        });

        // The system as the actor, for work no person asked for.
        services.AddSingleton<ISystemExecutionContextFactory, SystemExecutionContextFactory>();

        return services;
    }
}

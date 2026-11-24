using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NamespaceRoot.ProductName.Common.Application.Execution;
using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Security;

// TODO: Security/ keeps the actor model of the kernel - the users a request can act as, the actor a job
// or a consumer carries, and the headers that pass them between services. The 3.0 migration map sent the
// whole folder to Framework.Auth; what moved is the token half (Rsa* and its configuration). What stays
// is the kernel's own: IUserContext and IAuthContext are the kernel's contracts, AuthHeaders is the
// internal service protocol used by ten kernel files, JobActorContext is written by two capabilities
// (RabbitMQ, Hangfire) through the kernel reference, and BasicCredentials guards the Hangfire dashboard.
// Moving these would make the kernel reference Framework.Auth. ADR-009 allows a violation with its reason
// written; this is the reason.
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

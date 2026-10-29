using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Audit;

/// <summary>
/// Reads the acting user for a journal entry without ever failing the operation being audited.
/// </summary>
/// <remarks>
/// <para>
/// The actor comes from claims, and asking for a user id when there are no claims throws — which is
/// exactly the situation inside a Hangfire job or a bus consumer. Reading it eagerly turned the
/// audit trail into a dependency of the work: a scheduled job failed with "User ID claim is missing",
/// from a stack that ran through the interceptor.
/// </para>
/// <para>
/// A change nobody can be attributed to is still a change worth recording, so an unreadable actor
/// degrades to the system actor rather than aborting the save. The journal is a witness; a witness
/// does not get to stop the event.
/// </para>
/// </remarks>
internal static class AuditActorReader
{
    public static (Guid UserId, string? Login, Guid? TenantId) Read(IDomainExecutionContext execution)
    {
        var actor = execution.Actor;

        if (SafeIsSystem(actor)) return (Guid.Empty, null, SafeTenantId(actor));

        try
        {
            return (actor.UserId, SafeLogin(actor), SafeTenantId(actor));
        }
        catch (UnauthorizedAccessException)
        {
            // No claims at all — a job, a consumer, or a startup path. System is what it is.
            return (Guid.Empty, null, null);
        }
        catch (InvalidOperationException)
        {
            // Claims present but declared system, which is the same answer by another route.
            return (Guid.Empty, null, SafeTenantId(actor));
        }
    }

    private static bool SafeIsSystem(IUserContext actor)
    {
        try
        {
            return actor.IsSystemCall;
        }
        catch
        {
            return true;
        }
    }

    private static string? SafeLogin(IUserContext actor)
    {
        try
        {
            return actor.Login;
        }
        catch
        {
            return null;
        }
    }

    private static Guid? SafeTenantId(IUserContext actor)
    {
        try
        {
            return actor.TenantId;
        }
        catch
        {
            return null;
        }
    }
}

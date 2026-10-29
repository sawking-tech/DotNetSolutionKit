using NamespaceRoot.ProductName.Common.Domain.Context;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Security;

/// <summary>
/// The actor a background job runs as, published for the duration of that job.
/// </summary>
/// <remarks>
/// <para>
/// A job has no HTTP context, so <see cref="HttpUserContext"/> has no claims to read and throws when
/// asked for a user id. Until something needed the actor that stayed invisible; the audit journal
/// needed it, and a job started by hand failed with "User ID claim is missing".
/// </para>
/// <para>
/// Two kinds of job, two answers. One on a schedule has no person behind it and runs as the system.
/// One triggered through an internal endpoint does, and recording "the system did this" when an
/// administrator pressed the button puts a false actor in the audit trail. The triggering actor is carried in the job's parameters and restored here.
/// </para>
/// </remarks>
public static class JobActorContext
{
    private static readonly AsyncLocal<IUserContext?> Current = new();

    /// <summary>The actor of the running job, or <c>null</c> outside one.</summary>
    public static IUserContext? Actor => Current.Value;

    /// <summary>Publishes <paramref name="actor"/> for the duration of the returned scope.</summary>
    public static IDisposable Use(IUserContext actor)
    {
        var previous = Current.Value;
        Current.Value = actor;
        return new Scope(previous);
    }

    private sealed class Scope(IUserContext? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}

/// <summary>
/// The actor that enqueued a job, reconstructed from what was captured at enqueue time.
/// </summary>
/// <remarks>
/// Carries identity only — roles and permissions are deliberately empty. A job is not a place to
/// re-run authorisation: the endpoint that enqueued it already decided the caller was allowed, and
/// re-deriving rights from a snapshot hours old would be worse than not having them.
/// </remarks>
public sealed class JobTriggeredByUserContext(Guid userId, string? login, Guid? tenantId) : IUserContext
{
    public Guid UserId { get; } = userId;

    public Guid? TenantId { get; } = tenantId;

    public string? Login { get; } = login;

    public string? DisplayName => Login;

    public Guid? ApiKeyId => null;

    public IReadOnlyList<string> Roles { get; } = [];

    public IReadOnlyList<string> Permissions { get; } = [];

    public IAuthContext AuthContext { get; } =
        new AuthContext(AuthMethod.Jwt, userId.ToString(), DateTimeOffset.MaxValue);

    public bool IsJwtAuthenticated => true;

    public bool IsApiKeyAuthenticated => false;

    public bool IsSystemCall => false;
}

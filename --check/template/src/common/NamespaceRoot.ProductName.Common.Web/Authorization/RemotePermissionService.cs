using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NamespaceRoot.ProductName.Common.Application.Authorization;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Exceptions;

namespace NamespaceRoot.ProductName.Common.Web.Authorization;

/// <summary>
/// Asks a separate service for the current user's permissions, for a product whose permissions are kept
/// apart from the token (<c>Permissions:Source</c> = <c>remote</c>).
/// </summary>
/// <remarks>
/// The call goes through the internal service handlers, so the permission service sees the internal key and
/// the caller. An answer is reused for <see cref="RemotePermissionsOptions.CacheSeconds"/> per user. A
/// permission service that does not answer makes the request a 503 rather than a 403: the user may well
/// hold the permission, and a refusal would read as a decision. A system call holds every permission.
/// </remarks>
public sealed class RemotePermissionService(
    IHttpClientFactory httpClientFactory,
    IUserContext userContext,
    IMemoryCache cache,
    IOptions<PermissionsOptions> options) : IPermissionService
{
    public const string HttpClientName = "permissions";

    public async Task<bool> UserHasAllPermissionsAsync(
        IReadOnlyCollection<string> allRequiredPermissions,
        CancellationToken cancellationToken)
    {
        if (allRequiredPermissions.Count == 0 || userContext.IsSystemCall)
            return true;
        if (userContext.UserId == Guid.Empty)
            return false;

        var held = await HeldBy(userContext.UserId, cancellationToken);
        return allRequiredPermissions.All(held.Contains);
    }

    private async Task<HashSet<string>> HeldBy(Guid userId, CancellationToken cancellationToken)
    {
        var key = $"{nameof(RemotePermissionService)}:{userId}";
        if (cache.TryGetValue(key, out HashSet<string>? cached) && cached is not null)
            return cached;

        var remote = options.Value.Remote;
        var path = remote.Path.Replace("{userId}", userId.ToString());
        string[]? answer;
        try
        {
            answer = await httpClientFactory.CreateClient(HttpClientName)
                .GetFromJsonAsync<string[]>(path, cancellationToken);
        }
        // A timeout is a TaskCanceledException too; only the caller's own cancellation is not an outage.
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new ServiceUnavailableException("The permission service did not answer", ex, "PERMISSIONS_UNAVAILABLE");
        }

        var held = (answer ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        cache.Set(key, held, TimeSpan.FromSeconds(remote.CacheSeconds));
        return held;
    }
}

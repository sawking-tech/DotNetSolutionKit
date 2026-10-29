using Microsoft.Extensions.Logging;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Http.Internal;

/// <summary>
/// Puts the internal API key on a call to another service of the product, so the service trusts the
/// caller and the user context that comes with it. Not for calls outside the product: the key would leave
/// with them.
/// </summary>
public sealed class InternalApiKeyDelegatingHandler(
    IInternalApiConfiguration config,
    ILogger<InternalApiKeyDelegatingHandler> logger) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Headers.Contains(AuthHeaders.ApiKey))
        {
            request.Headers.TryAddWithoutValidation(AuthHeaders.ApiKey, config.ApiKey);
            logger.LogTrace("Internal API key put on the call to {Uri}", request.RequestUri);
        }

        return base.SendAsync(request, cancellationToken);
    }
}

using Microsoft.Net.Http.Headers;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Web.Gateway;
using Yarp.ReverseProxy.Transforms;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.API.Setup;

/// <summary>
/// What the gateway changes in a request on its way to a service.
/// </summary>
public static class GatewayTransforms
{
    public static IReverseProxyBuilder AddGatewayTransforms(this IReverseProxyBuilder proxy) =>
        proxy.AddTransforms(context =>
        {
            // CORS is decided here, by the gateway's Cors section: the gateway answers the preflight and sets
            // the headers of the answer. A service that saw the Origin would add its own CORS headers, YARP
            // would pass them over the gateway's, and a site the gateway refuses could read the answer.
            context.AddRequestHeaderRemove(HeaderNames.Origin);

            // And whatever a service sets anyway - one that answers CORS without looking at the Origin - is
            // dropped. The gateway's own CORS headers are added later, when the answer starts.
            context.AddResponseTransform(transform =>
            {
                var headers = transform.HttpContext.Response.Headers;
                foreach (var name in headers.Keys.Where(IsCors).ToList())
                    headers.Remove(name);
                return ValueTask.CompletedTask;
            });

            context.AddRequestTransform(transform =>
            {
                var internalApiKey = transform.HttpContext.RequestServices
                    .GetRequiredService<IInternalApiConfiguration>().ApiKey;
                GatewayForwarding.ForwardUser(transform.ProxyRequest.Headers, transform.HttpContext.User, internalApiKey);
                return ValueTask.CompletedTask;
            });
        });

    private static bool IsCors(string header) =>
        header.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase);
}

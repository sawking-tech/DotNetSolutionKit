using NamespaceRoot.ProductName.Framework.Auth.Authentication;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.API.Setup;

/// <summary>
/// Authentication and authorization for this service, by the shared composite scheme of
/// <see cref="ServiceAuthenticationSetup"/>, which says which request goes where, and the default API key handler
/// of the framework. A team that authenticates its callers its own way names its own handler here.
/// The middleware is added by the platform pipeline.
/// </summary>
internal static class Authentication
{
    public static WebApplicationBuilder SetupAppAuthentication(this WebApplicationBuilder builder)
    {
        builder.SetupServiceAuthentication<ApiKeyAuthenticationHandler>();
        builder.Services.AddAuthorization();
        return builder;
    }
}

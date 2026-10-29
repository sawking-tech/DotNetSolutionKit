using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;

namespace NamespaceRoot.ProductName.Common.Web.Authentication;

/// <summary>
/// Unified authentication setup for all services: one composite scheme. A request with a token - the
/// Bearer header or the access-token cookie - goes to JWT, registered only when "Jwt:PublicKeyPath" is
/// configured; any other goes to the API key handler: calls between services and from the platform. The
/// Gateway forwards the caller it verified with its internal key and passes the token on, so a service
/// without "Jwt:PublicKeyPath", the default, takes it by the key, and one with it validates the token.
/// </summary>
public static class ServiceAuthenticationSetup
{
    public const string ApiKeySchemeName = "ApiKey";
    public const string CompositeSchemeName = "ApiKey_Or_Jwt";

    /// <summary>
    /// Cookie name for the access JWT. Set by the Auth service on login/refresh via
    /// <see cref="AuthCookieExtensions.SetAccessTokenCookie"/> and read here as an
    /// alternative to the Authorization header (browsers that keep the token in an HttpOnly
    /// cookie cannot attach a Bearer header themselves).
    /// </summary>
    public const string AccessTokenCookieName = "access_token";

    /// <summary>
    /// Registers the composite authentication scheme (API Key + optional JWT) for the service.
    /// </summary>
    public static WebApplicationBuilder SetupServiceAuthentication<TApiKeyHandler>(
        this WebApplicationBuilder builder)
        where TApiKeyHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        var jwtConfig = builder.Configuration
            .GetSection(JwtPublicConfiguration.SectionName)
            .Get<JwtPublicConfiguration>();
        var jwtEnabled = !string.IsNullOrWhiteSpace(jwtConfig?.PublicKeyPath);

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CompositeSchemeName;
            options.DefaultChallengeScheme = CompositeSchemeName;
        })
        .AddPolicyScheme(CompositeSchemeName, "API Key or JWT", options =>
        {
            options.ForwardDefaultSelector = context =>
            {
                if (jwtEnabled)
                {
                    var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
                    if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
                        return JwtBearerDefaults.AuthenticationScheme;

                    // Fallback for browser clients that keep the JWT in an HttpOnly cookie
                    // (see AuthCookieExtensions). Cookie-only requests carry no Authorization
                    // header, so the composite scheme has to opt into the JWT scheme itself.
                    if (!string.IsNullOrEmpty(context.Request.Cookies[AccessTokenCookieName]))
                        return JwtBearerDefaults.AuthenticationScheme;
                }

                return ApiKeySchemeName;
            };
        })
        .AddScheme<AuthenticationSchemeOptions, TApiKeyHandler>(ApiKeySchemeName, _ => { });

        if (jwtEnabled)
            builder.AddPlatformJwtBearer();

        return builder;
    }

    /// <summary>
    /// Authentication for a gateway: every caller presents a JWT, validated here; the services behind
    /// trust what the gateway forwards with the internal API key.
    /// </summary>
    public static WebApplicationBuilder SetupGatewayAuthentication(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme);
        builder.AddPlatformJwtBearer();
        return builder;
    }

    /// <summary>
    /// The JWT bearer scheme: RS256 tokens checked against the public key at <c>Jwt:PublicKeyPath</c>,
    /// from the Authorization header or the access token cookie.
    /// </summary>
    private static void AddPlatformJwtBearer(this WebApplicationBuilder builder)
    {
        builder.Services.AddJwtPublicConfiguration(builder.Configuration);
        builder.Services.AddSingleton<IRsaPublicKeyProvider, RsaPublicKeyProvider>();
        builder.Services.AddAuthentication().AddJwtBearer();
        builder.Services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IRsaPublicKeyProvider, IJwtPublicConfiguration>((options, rsaProvider, jwt) =>
            {
                // Claims keep the names the token uses (role, permissions, user_id), the names the
                // rest of the platform reads; by default "role" would be renamed to a long URI.
                options.MapInboundClaims = false;

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        // Header wins; fall back to the HttpOnly access token cookie so
                        // browser clients that never touch localStorage keep working.
                        if (string.IsNullOrEmpty(context.Token))
                        {
                            var cookie = context.Request.Cookies[AccessTokenCookieName];
                            if (!string.IsNullOrEmpty(cookie))
                                context.Token = cookie;
                        }
                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = context =>
                    {
                        // Do NOT write to Response here — let the error handling middleware handle it.
                        context.HttpContext.Items["AuthError"] = "INVALID_TOKEN";
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        // Only annotate the error — do NOT call HandleResponse().
                        context.HttpContext.Items["AuthError"] = context.AuthenticateFailure != null
                            ? "INVALID_TOKEN"
                            : "NO_TOKEN";
                        return Task.CompletedTask;
                    }
                };

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ClockSkew = TimeSpan.Zero,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = rsaProvider.ValidationKey
                };
            });

    }

    /// <summary>
    /// Adds authentication middleware to the pipeline.
    /// </summary>
    public static WebApplication UseServiceAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        return app;
    }
}

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Infrastructure.Security.Handlers;

public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IInternalApiConfiguration _internalApiConfig;
    private readonly ILogger<ApiKeyAuthenticationHandler> _logger;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IInternalApiConfiguration internalApiConfig)
        : base(options, logger, encoder)
    {
        _internalApiConfig = internalApiConfig;
        _logger = logger.CreateLogger<ApiKeyAuthenticationHandler>();
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(AuthHeaders.ApiKey, out var apiKeyHeader))
        {
            _logger.LogTrace("No API key header found");
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var apiKey = apiKeyHeader.ToString();

        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogWarning("Empty API key provided");
            return Task.FromResult(AuthenticateResult.Fail("Empty API key"));
        }

        // Use constant-time comparison for internal API key to prevent timing attacks
        if (TimeSafeCompare(apiKey, _internalApiConfig.ApiKey))
        {
            _logger.LogDebug("Internal API key detected");

            var authenticateResult = IsSystemCall()
                ? ProcessSystemCall()
                : ProcessUserContextViaInternalApiKey();

            // Ensure HttpContext.User is set for all authentication results
            if (authenticateResult.Succeeded) Context.User = authenticateResult.Principal;

            return Task.FromResult(authenticateResult);
        }

        // NOTE: Here you can add additional API key validation logic for specific keys if needed
        // For example: checking against a database of valid API keys or external validation service
        // Example:
        // if (TimeSafeCompare(apiKey, "specific-key-1") || TimeSafeCompare(apiKey, "specific-key-2"))
        // {
        //     return ProcessSpecificApiKey(apiKey);
        // }

        _logger.LogWarning("Invalid API key provided");
        return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
    }

    private bool IsSystemCall()
    {
        return Request.Headers.ContainsKey(AuthHeaders.SystemCall) ||
               Request.Path.StartsWithSegments("/api/system") ||
               Request.Path.StartsWithSegments("/internal");
    }

    private AuthenticateResult ProcessSystemCall()
    {
        _logger.LogDebug("Processing system call authentication");

        var claims = new List<Claim>
        {
            new(AuthClaims.AuthType, AuthMethod.System.ToString()),
            new(AuthClaims.SystemCall, "true"),
            new(ClaimTypes.AuthenticationMethod, "SystemApiKey"),
            new(AuthClaims.UserId, Guid.Empty.ToString())
        };

        return CreateAuthenticationTicket(claims, "SystemApiKey");
    }

    private AuthenticateResult ProcessUserContextViaInternalApiKey()
    {
        _logger.LogDebug("Processing user context via internal API key");

        // Validate required headers for user context
        if (!Request.Headers.TryGetValue(AuthHeaders.UserId, out var userIdHeader) ||
            string.IsNullOrEmpty(userIdHeader))
        {
            _logger.LogWarning("Missing or empty UserId header for internal API key context");
            return AuthenticateResult.Fail("UserId header is required for user context");
        }

        var claims = new List<Claim>
        {
            new(AuthClaims.AuthType, AuthMethod.ApiKey.ToString()),
            new(AuthClaims.SystemCall, "false"),
            new(ClaimTypes.AuthenticationMethod, "InternalApiKey"),
            new(AuthClaims.UserId, userIdHeader.ToString())
        };

        // Add mandatory AuthId claim from header or generate one
        if (Request.Headers.TryGetValue(AuthHeaders.AuthId, out var authIdHeader) &&
            !string.IsNullOrEmpty(authIdHeader))
        {
            claims.Add(new Claim(AuthClaims.AuthId, authIdHeader.ToString()));
            claims.Add(new Claim(AuthClaims.ApiKeyId, authIdHeader.ToString()));
        }
        else
        {
            var generatedAuthId = $"internal-api-key-{userIdHeader}";
            claims.Add(new Claim(AuthClaims.AuthId, generatedAuthId));
            claims.Add(new Claim(AuthClaims.ApiKeyId, generatedAuthId));
        }

        // Add optional headers as claims
        AddOptionalHeaderClaim(AuthHeaders.UserLogin, AuthClaims.UserLogin, claims);
        AddOptionalHeaderClaim(AuthHeaders.UserDisplayName, AuthClaims.DisplayName, claims);
        AddOptionalHeaderClaim(AuthHeaders.TenantId, AuthClaims.TenantId, claims);
        AddOptionalHeaderClaim(AuthHeaders.AuthExp, AuthClaims.Exp, claims);
        AddOptionalHeaderClaim(AuthHeaders.AuthExp, AuthClaims.AuthExp, claims);
        AddOptionalHeaderClaim(AuthHeaders.AuthValidated, AuthClaims.AuthValidated, claims);

        // Add roles from header
        if (Request.Headers.TryGetValue(AuthHeaders.UserRoles, out var rolesHeader))
        {
            var roles = rolesHeader.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(role => role.Trim())
                .Where(role => !string.IsNullOrEmpty(role)).ToList();

            claims.AddRange(roles.Select(role => new Claim(AuthClaims.UserRole, role)));

            _logger.LogDebug("Added {RoleCount} roles from header for user {UserId}", roles.Count, userIdHeader);
        }

        // Permissions from the gateway, which read them from the caller's token
        if (Request.Headers.TryGetValue(AuthHeaders.UserPermissions, out var permissionsHeader))
        {
            claims.AddRange(permissionsHeader.ToString()
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(permission => new Claim(AuthClaims.Permissions, permission)));
        }

        // Add default role if no roles provided
        if (claims.All(c => c.Type != AuthClaims.UserRole))
        {
            claims.Add(new Claim(AuthClaims.UserRole, "User"));
            _logger.LogDebug("Added default User role for internal API key user {UserId}", userIdHeader.ToString());
        }

        return CreateAuthenticationTicket(claims, "InternalApiKey");
    }

    private void AddOptionalHeaderClaim(string headerName, string claimType, List<Claim> claims)
    {
        if (Request.Headers.TryGetValue(headerName, out var headerValue) &&
            !string.IsNullOrEmpty(headerValue))
            claims.Add(new Claim(claimType, headerValue.ToString()));
    }

    private AuthenticateResult CreateAuthenticationTicket(List<Claim> claims, string authenticationMethod)
    {
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, authenticationMethod);

        _logger.LogDebug("Created authentication ticket with {Count} claims for method {AuthMethod}",
            claims.Count, authenticationMethod);

        return AuthenticateResult.Success(ticket);
    }

    /// <summary>
    /// Constant-time string comparison to prevent timing attacks
    /// </summary>
    private static bool TimeSafeCompare(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return false;

        if (a.Length != b.Length)
            return false;

        var result = 0;
        for (var i = 0; i < a.Length; i++) result |= a[i] ^ b[i];

        return result == 0;
    }
}

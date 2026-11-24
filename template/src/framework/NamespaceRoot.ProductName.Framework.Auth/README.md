# Auth Module

The authentication and permission surface every service of the solution shares: the bearer
validation a service runs, the cookies a browser-facing service issues, the API key a service
accepts from another service, and the permission checks a controller declares.

This project is the detachable seam of the system, a candidate for a package of its own. What is
deliberately not here: the token endpoints - who a user is and what a token carries is the
product's, and the module only validates what it receives; the permission catalogue - the module
reads the claims of the token or asks the service that owns them.

### What is inside

- `ServiceAuthenticationSetup` - `SetupServiceAuthentication<TApiKeyHandler>` for a service,
  `SetupGatewayAuthentication` for the gateway, and `UseServiceAuthentication` in the pipeline.
- `AuthCookieExtensions` / `AuthCookieSettings` - the access and refresh token cookies, their
  names and lifetimes, issuing and clearing them.
- `CsrfProtectionMiddleware` - the double-submit check the cookie authentication needs.
- `PermissionAuthorizationFilter` / `RequiredPermissionsAttribute` - `[RequiredPermissions(...)]`
  on a controller or an action, refused before the body runs.
- `ClaimsPermissionService` / `RemotePermissionService` - permissions from the token, or from the
  service that owns them, chosen by `PermissionsOptions` / `RemotePermissionsOptions`.
- `PermissionsServiceCollectionExtensions` - `AddPlatformPermissions` wires the choice.
- `IPermissionService` and its implementations - the seam the filter asks; a service whose
  permissions live elsewhere registers its own implementation.
- `Configuration/` - the JWT, refresh-token and auth-validation options, bound and validated at
  startup by `AuthConfigurationExtensions` (`AddJwtPublicConfiguration`, `AddRefreshTokenConfiguration`,
  `AddAuthValidationConfiguration`).
- `Security/` - the RSA key providers (`RsaKeyProvider`, `RsaPublicKeyProvider`) that read the
  public and private keys the token validation and signing need.

### Usage

```csharp
builder.SetupServiceAuthentication<ApiKeyAuthenticationHandler>();
...
app.UseServiceAuthentication();
```

A controller action that needs a permission:

```csharp
[RequiredPermissions("orders.read")]
public IActionResult List() => Ok();
```

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.

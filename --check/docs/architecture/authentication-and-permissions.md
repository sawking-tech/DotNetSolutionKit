# Authentication and permissions

## Authentication

A service authenticates every request itself, with one composite scheme:

- a JWT, from the `Authorization: Bearer` header or the access-token cookie, when `Jwt:PublicKeyPath`
  is set. A service that only validates tokens needs the public key; the signing key stays with the
  service that issues them;
- an `X-API-Key` for every other request: calls between services and from the platform itself.

A request with a token goes to the JWT scheme when it is configured, any other to the API key handler.
The [gateway](../features/api-gateway.md) forwards the caller it verified with its internal key and passes
the token on: a service without `Jwt:PublicKeyPath`, the default, takes the request by the key; a service
with it validates the token again.

`Common.Web` has helpers that set and clear the access and refresh token cookies.

A generated service registers its API key handler in `Setup/Authentication.cs`:

```csharp
builder.SetupServiceAuthentication<ApiKeyAuthenticationHandler>();
builder.Services.AddAuthorization();
```

## Permissions

An action states the permissions it needs:

```csharp
[HttpPost]
[RequiredPermissions("orders.create")]
public Task<OrderResponse> Create(CreateOrderRequest request) => service.CreateAsync(request);
```

A global filter checks them before the action:

| Caller | Answer |
|---|---|
| anonymous | 401 |
| authenticated, missing a permission | 403, a problem naming the permissions |
| authenticated, holding every permission | the action runs |
| a system call, made by the platform itself | the action runs |

An action without `[RequiredPermissions]` is not checked. Swagger lists the permissions of each operation.

By default the permissions come from the token: `ClaimsPermissionService` reads the `permissions` claims,
one claim per permission, as a JSON array in a JWT becomes. It needs no other service, so it works for a
solution with a single service. A service whose permissions live elsewhere registers its own
`IPermissionService`.

Behind a [gateway](../features/api-gateway.md) the token is validated once, at the gateway, and a service
receives the user and the permissions in headers that come with the internal API key; the same filter
and the same `ClaimsPermissionService` check them.

### Permissions from a separate service

A product that keeps permissions apart from the token sets `Permissions:Source` to `remote`:

```json
"Permissions": {
  "Source": "remote",
  "Remote": {
    "BaseAddress": "http://permissions:8080/",
    "Path": "/api/v1/permissions/users/{userId}",
    "CacheSeconds": 30
  }
}
```

`RemotePermissionService` then asks that service for the user's permissions, a JSON array of strings,
with the internal key and the caller, as any [call to another service](#calling-another-service). An
answer is reused for `CacheSeconds` per user, so a permission taken away stops working within that time.

| The permission service | Answer |
|---|---|
| lists every permission the action needs | the action runs |
| leaves one out | 403 |
| does not answer, or answers an error | 503 `PERMISSIONS_UNAVAILABLE`: the user may hold the permission, and a refusal would read as a decision |

A system call holds every permission without asking. `remote` without `BaseAddress`, or a `Source` other
than `claims` or `remote`, stops the service at start.

## Tokens in cookies

A token in a response body ends up in the frontend's storage, where any script on the page can read it.
The service that issues tokens puts them into HttpOnly cookies with `AuthCookieExtensions` instead:

- login answers with the user and their permissions, and `IssueTokenCookies` sets the access and refresh
  cookies;
- refresh reads `ReadRefreshTokenCookie`, rotates both tokens into the cookies, and answers with an empty
  body;
- logout calls `ClearTokenCookies`; a token error clears both cookies too, and answers 401.

The access cookie authenticates every request, read where no `Authorization` header is sent. A rule test
in a generated service, `ResponseContractTests`, fails when an action returns a type with a property named
`Token`, `AccessToken`, `RefreshToken`, `IdToken`, `Jwt` or `BearerToken`.

What the cookie can be depends on where the frontend runs; the browser enforces it:

| Frontend and API | HTTPS | `AuthCookies` | CSRF |
|---|---|---|---|
| same site: one domain, or subdomains of one; ports do not matter | either | `SameSite: Lax` | the browser keeps a Lax cookie off cross-site POSTs; the header check is a second layer |
| different domains | yes | `SameSite: None`, `ServedOverHttps: true` | the header check is the protection: the cookie reaches cross-site requests |
| different domains | no | not possible: the browser drops `SameSite=None` without `Secure` | put both behind one domain, a reverse proxy or the [gateway](../features/api-gateway.md), or serve HTTPS |

`SameSite: None` without `ServedOverHttps` refuses to start, and so does `None` with `Cors:AllowedOrigins`
`*` and credentials: any site could then send the cookie and the header.

With `RequireCsrfHeader`, a POST, PUT, PATCH or DELETE that a token cookie would authenticate needs the
`X-CSRF` header, any value, or is refused with 403 `CSRF_HEADER_MISSING`. A page on another origin cannot
add a custom header without a CORS preflight, which only the allowed origins pass. A request with an
`Authorization` header or an API key is not checked. The frontend sends the header on every such request:

```ts
fetch(url, { method: "POST", credentials: "include", headers: { "X-CSRF": "1" }, body })
```

A service generated from 2.4 has the section in `appsettings.json` with `Lax` and the check on. Without
the section, as in a solution generated earlier, the cookies behave as they did: `SameSite=None` over
HTTPS, `Lax` over HTTP, and no CSRF check. Such a solution turns the protection on by adding the section
once its frontend sends the header.

## Calling another service

A client the factory builds, typed or Refit, becomes a call to another service of the product with one
line:

```csharp
services.AddHttpClient<BillingClient>(c => c.BaseAddress = new Uri(options.BaseUrl))
    .AddInternalServiceHandlers();
```

Every request of that client then carries:

- the internal API key;
- the caller, read from the request's claims: the user, the tenant, the roles and the permissions, so
  the called service checks its permissions as it checks the gateway's headers. Work with no request
  behind it, a job or a consumer, calls as the system. An anonymous request passes nobody on, so an open
  endpoint does not get the system's rights in the next service;
- the [correlation identifier](web-layer.md#correlation) of the running work.

A client for a provider outside the product takes `.AddCorrelation()` alone: the key and the user's
details are not the provider's to see.

## Tests

`Common.Tests` checks on a test server an open action, an anonymous caller, a missing and a held
permission, and a system call. For a call to another service it checks, through a client the factory
builds, what goes with the request for a user, a job, a system caller and an anonymous request.

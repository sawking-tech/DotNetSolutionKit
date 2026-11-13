# Observability Module

The correlation and tracing a request carries through the services: one correlation id taken from
the incoming call or made for it, put on the response, passed to the next service, and reachable
from the code that writes a log or a bus message.

This project is the detachable seam of the system, a candidate for a package of its own. What is
deliberately not here: the Serilog setup itself - the sinks, the enrichers and the templates live
in `NamespaceRoot.ProductName.Common`, where the team tunes them; the module holds the correlation
and the version of the Serilog packages the solution logs with.

### What is inside

- `ICorrelationContext` / `HttpCorrelationContext` - the correlation id of the current request.
- `CorrelationIdMiddleware` - reads the incoming header, makes an id when there is none, and puts
  it on the response.
- `CorrelationDelegatingHandler` - the outgoing `HttpClient` carries the id to the next service.
- `Correlation` - `Correlation.Use(id)` puts an id around a scope that is not a request, a job or
  a consumer.
- `TracingHeaders` / `TracingProperties` - the header and property names, in one place.

### Usage

The web pipeline installs `CorrelationIdMiddleware`, and the internal `HttpClient` a
service-to-service call uses carries `CorrelationDelegatingHandler`. Code outside a request - a job,
a consumer, a loop - puts the id around its own scope:

```csharp
using (Correlation.Use(correlationId))
{
    // ...
}
```

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.

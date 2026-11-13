# OpenApi Module

The API documents a service and the gateway publish: the versioned Swagger documents built from
the controllers, the filters that make them readable, the gateway page that lists the services,
and the schema dump the solution's contract checks read.

This project is the detachable seam of the system, a candidate for a package of its own. What is
deliberately not here: the controllers and their contracts - the document is generated from them;
the API-diff check itself - the module gives it the dump, the script compares the dumps.

### What is inside

- `SwaggerSetup` - `SetupSwaggerPage` and `UseSwaggerPage`: one versioned document per API version,
  the UI on top of them.
- `SwaggerSettings` / `SwaggerServer` / `SwaggerServers` - the document's servers and the switches
  that shape it.
- Filters: `AuthorizationOperationFilter`, `PermissionsOperationFilter`, `SchemaValuesFromFilter`,
  `ScrubDescriptionsFilter`, `VersionedDocumentFilter`.
- `ApiVersionHelper` - the versions found in the assembly and in a route.
- `GatewaySwagger` - the gateway's own page, built from the reverse-proxy routes.
- `SchemaDump` / `SchemaOnlyMode` - write the document to a file for the contract check, and start
  a host for that alone.

### Usage

```csharp
builder.SetupSwaggerPage(assembly);
...
app.UseSwaggerPage(assembly);
```

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.

# Settings

A service reads its settings from these sources, each overriding the ones before it:

1. `features.json` from Common, with `--FeatureFlags`;
2. `appsettings.json`, then `appsettings.<Environment>.json`, then `appsettings.Secrets.json` in `Local`;
3. environment variables;
4. the secret store, with `-I` or `--Vault`: the shared folder, then the service's
   ([secrets](../features/secrets.md)).

## What changes while the service runs

The JSON files are read again when they change on disk, and the secret store every
`ReloadSeconds` (300 by default; 0 reads it at startup only). A new value reaches what reads it on
each use, and nothing else:

| Changes without a restart | Needs a restart |
|---|---|
| [feature flags](../features/feature-flags.md), read on each check | connection strings: the database, the bus, ClickHouse, MongoDB, object storage |
| settings registered with `AddReloadableOptions` and read through `IReloadable<T>` | the JWT keys and issuer, the internal API key, CORS, the permission source |
| | settings registered with `AddValidatedOptions`, the job server, logging |

Environment variables do not change in a running process; a value set there takes a restart in any case.

## A setting that changes at runtime

A setting read on each use, such as a limit or a list of allowed values, is registered as reloadable:

```csharp
services.AddReloadableOptions<ImportLimits>("ImportLimits");
```

```csharp
public sealed class ImportService(IReloadable<ImportLimits> limits)
{
    public bool Fits(int rows) => rows <= limits.Current.MaxRows;
}
```

It is validated by its data annotations at startup like any other setting, and a service with an invalid
value does not start. Afterwards each change of its section is bound and validated again: a valid value
replaces `Current`, and an invalid one is logged as a warning and dropped: the service goes on with the
last valid value, and a typo in a file or the store fails no request.

`AddValidatedOptions` stays for what is built once from a value: the value is read at the first use and
kept for the life of the process.

## The secret store, read again

The reload reads the same folders as the start, in the same order. New values replace the old ones and
raise the configuration's change token; the same values raise nothing. When the store does not answer,
the service keeps the values it has, and `Secrets:ReloadError` holds when and why the last reload failed.

## Tests

`Common.Tests` covers a reloadable setting taking a valid value and dropping an invalid one, and the
store's reload: a changed value reaching configuration and raising the token, the same values raising
nothing, and an unreachable store leaving the values in place.

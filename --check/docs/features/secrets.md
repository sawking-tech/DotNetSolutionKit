# Secrets from Infisical or Vault

Generated with `-I` for [Infisical](https://infisical.com) or `--Vault` for [HashiCorp Vault](https://www.vaultproject.io),
off by default. Pass the flag with `--Solution`, and to each service generated later that reads secrets.
What follows holds for both; [Vault](#hashicorp-vault) has its own section for what differs.

The service reads two folders of an Infisical project, the shared one and its own,
and lays them over the rest of configuration. Code reads a secret like any other setting: a secret named
`ConnectionStrings__DefaultConnection` is `configuration.GetConnectionString("DefaultConnection")`.

Keep both secrets and ordinary settings in the store. A value there changes without a new build or a
deployment: the service reads the store again every `ReloadSeconds` (300 by default), and a setting it
reads on each use takes the new value without a restart; see [settings](../architecture/settings.md).

## Order

1. `appsettings.json`, `appsettings.<Environment>.json`, `appsettings.Secrets.json` in `Local`;
2. environment variables;
3. the shared folder (`/` by default);
4. the service's folder (`/orders`, or `/sales_orders` for `Sales.Orders`).

The store comes last, so a value it holds wins over anything shipped in the image, and the service's
folder wins over the shared one: a service overrides a shared value without the shared folder having to
know which services exist.

## Configuration

```json
"Infisical": {
  "ProjectId": "",
  "EnvironmentSlug": ""
}
```

`ProjectId` and `EnvironmentSlug` (`dev`, `staging`, `prod`) are required outside `Local`. `HostUri`,
`SharedPath`, `ServicePath` and `Optional` have defaults.

The machine identity, `Infisical__ClientId` and `Infisical__ClientSecret`, comes from environment
variables only. A file in the repository holding the key to the store would bring back the problem the
store solves.

## HashiCorp Vault

With `--Vault` the two folders are two key-value secrets of a KV version 2 engine: the shared one,
`shared` by default, and the service's, `orders` or `sales_orders` for `Sales.Orders`. Each key of a
secret is one setting, named as above.

```json
"Vault": {
  "Address": ""
}
```

`Address` is required outside `Local`. `Mount` (`secret`), `SharedPath`, `ServicePath` and `Optional` have
defaults. The service signs in with a token, `Vault__Token`, or with an AppRole,
`Vault__RoleId` and `Vault__SecretId`, from environment variables only. A path without a secret reads as
empty, as an empty folder does, so a service with no secrets of its own needs none created; a refusal or
an unreachable Vault is handled as below.

## Why not environment variables

Without `-I`, a service takes its secrets from environment variables, and so do the defaults of this
template (`ConnectionStrings__DefaultConnection`, `RabbitMq__Password`). They are simple, but an
environment variable is readable by anyone with access to the host or the container: `docker inspect`
prints it, `/proc/<pid>/environ` holds it, a process dump contains it, and a CI step that prints the
environment publishes it in its log. A secret in an environment variable is as safe as the least
protected way into that machine.

With `-I`, the secrets live in the store and reach the process only in memory. One secret still has to
come from outside: the machine identity that lets the service read the store. Keep its damage small:

- give the identity read access to one project and one environment only;
- rotate it, and revoke it when a host is retired.

## Why Infisical

The problem it solves: a successful break-in to any one server should not hand over the keys of the
whole system. The internal API key, the key that signs tokens, the broker's credentials sit in the
environment of every server that uses them; with a store, they live in one place with access control, and
the environment of a server holds only the identity that reads its share.

The broker's credentials are the ones most often underrated. Whoever holds them can publish commands and
events that the services carry out as their own, actor headers included, without passing the API, its
permission checks or its request log. A stolen database password is noticed when data leaks; messages
sent through the broker can change things quietly.

The alternatives considered:

- HashiCorp Vault. Mature and widely integrated, but its licence is no longer open source, and it
  brings enterprise complexity for a team that needs a store of secrets and settings.
- OpenBao, the open-source fork of Vault under MPL-2.0. The licence problem is gone; the complexity
  stays.
- Infisical. Open source, simpler, and noticeably lighter to run than Vault, for a team that hosts it
  itself.

The template reads the store through a port, so another store can be added later; see the
[roadmap](../roadmap.md).

## When the store is unreachable

In `Local` the store is optional: a developer runs from `appsettings.Secrets.json` without it. Everywhere
else the service refuses to start without its secrets. A service that starts with a missing connection
string or key fails later, on a request, and further from the cause.

The store is read at startup and again every `ReloadSeconds`. A secret changed in the store reaches what
reads it on each use within that time; what was built from it at startup, such as a connection pool,
keeps the old value until the service restarts.

The secrets live in their source - `appsettings.Secrets.json` in `Local`, the store elsewhere - and in the
memory of a running service; nothing copies them anywhere else. A store that goes down while the service
runs leaves it the values it has. A service that starts while the store is down has no secrets, and
outside `Local` it does not start.

## Tests

`Common.Tests` covers the order of the folders and the optional and required store, with the store
replaced by a stub. With `--Vault`, the same rules are checked through the `Vault`
section, and the Vault store itself against a real Vault in development mode (`TEST_VAULT`) in the
integration tests of the template's CI and of a generated solution's.

# Capabilities.Secrets

The secret stores of the flags `--Infisical` and `--Vault`: the configuration providers that overlay a
service's configuration with the shared folder (or path) and the service's own, behind `ISecretStore`.
A service with one of the flags references this project, so only that service carries the store's SDK.

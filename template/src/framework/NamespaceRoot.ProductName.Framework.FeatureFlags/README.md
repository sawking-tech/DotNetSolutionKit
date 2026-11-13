# Feature Flags Module

The platform feature flags of a generated solution: the shared catalogue read from one
`features.json`, the Microsoft.FeatureManagement wiring on top of it, and the web surface that
answers about the flags. A feature means one thing across the platform: every service reads the
same file, and a service gains a new flag without a line of code - it adds a constant to
`FeatureKeys` only for the flags its own code reads.

This project is a detachable seam of the system, a candidate for a package of its own. What is
deliberately not here: the secret-store integration of the flags - the
`NamespaceRoot.ProductName.Capabilities.Security.CredentialStore` capability feeds the catalogue
from Infisical or Vault when `--SecretStore` is on; the module itself depends on nothing but
configuration.

### What is inside

- `FeatureDescriptor` / `FeatureEffect` / `FeatureState` / `IFeatureCatalog` - the primitives:
  a feature is declared with its effect, the catalogue answers what is on.
- `ConfigurationFeatureCatalog` - reads `features.json` (copied next to the binaries) and
  answers from it.
- `FeatureKeys` - the constants a service's own code reads; the shared file carries the flags,
  the keys are typed.
- `FeatureManagementServiceCollectionExtensions` / `CatalogFeatureDefinitionProvider` /
  `FeatureConfigurationExtensions` - the bridge to `Microsoft.FeatureManagement`:
  `AddPlatformFeatureManagement` registers the catalogue, the store and the library.
- `ConfigurationFeatureCatalog` is also an `IConfigurationProvider` source:
  `AddPlatformFeatures` puts the flags first into the configuration pipeline, so everything
  below can override them.
- `BehindFeatureAttribute` - the marker for code that exists only because a flag does.
- `FeatureGateFilters` - the Swagger filters that show a `[FeatureGate]` in the contract.
- `FeaturesController` - `GET /api/v1/features`, the feature list endpoint that ships with the
  platform; every service answers about flags the same way.
- `PinnedFeaturesAnnouncer` - logs the pinned flags at startup, so a wrong pin is seen, not guessed.

### Usage

The service host wires everything (under `--FeatureFlags`):

```C#
// configuration: flags first, so appsettings and the environment override them
builder.Configuration.SetupAppConfiguration(builder.Environment, args);

// DI: catalogue + Microsoft.FeatureManagement
services.AddPlatformFeatureManagement();
```

A controller gated by a flag, and a class that exists only for one:

```C#
[HttpGet("report")]
[FeatureGate(FeatureKeys.AdvancedReporting)]
public IActionResult Report() => Ok();

[BehindFeature(FeatureKeys.AdvancedReporting)]
public sealed class AdvancedReportBuilder : IReportBuilder { }
```

A new flag: add it to `features.json` (the shared file), gate an endpoint with `[FeatureGate]`
or read it through `IFeatureManager`; add the key to `FeatureKeys` if the service's own code
reads it. Mark the code that exists only for the flag with `[BehindFeature]`, so retiring the
flag later is a search, not an excavation.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.

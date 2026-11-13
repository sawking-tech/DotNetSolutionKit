# Validation Module

The FluentValidation wiring of a service: the validators of the commands are found in the
application assemblies and run before a handler, and the MVC auto-validation answers a bad request
in the same problem shape as every other error.

This project is the detachable seam of the system, a candidate for a package of its own. What is
deliberately not here: the validators themselves - they belong to the command they validate and
live beside it in the application layer; the command pipeline that calls them - it stays in
`NamespaceRoot.ProductName.Common.Application`.

### What is inside

- `ValidationSetup` - `AddValidation(assemblies)` registers FluentValidation, the validators of the
  given assemblies, and the MVC auto-validation.

### Usage

```csharp
services.AddValidation(typeof(ApplicationMarker).Assembly);
```

A command's validator stays with the command:

```csharp
public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
{
    public PlaceOrderValidator() => RuleFor(x => x.Quantity).GreaterThan(0);
}
```

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.

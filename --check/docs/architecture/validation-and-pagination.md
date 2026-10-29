# Validation and pagination

An invalid argument is refused before the action runs, with 422 and a validation problem naming the
fields:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 422,
  "errors": { "name": ["'Name' must not be empty."] },
  "code": "VALIDATION_ERROR",
  "correlationId": "..."
}
```

The same answer comes from each of the checks below, so a client handles one kind of input error.

## Validators

Validators are FluentValidation classes in the service's API assembly. `AddPlatformWebApi` finds them by
scanning, so a validator that exists is always registered, and SharpGrip runs them before the action:

```csharp
public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrderRequest>
{
    public PlaceOrderValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty();
        RuleFor(x => x.Lines).NotNull().Must(lines => lines!.Count > 0);
    }
}
```

A rule chain stops at its first failure, so `NotNull().Must(...)` does not dereference the null it just
rejected and throw out of the validator.

A value that does not bind, such as text where a number is expected or a body that is not valid JSON,
answers the same way.

## Text people read

A field one person types and another reads (a name, a comment, a message) is checked after
normalisation, so an invisible character cannot hide markup from the check:

```csharp
RuleFor(c => c.Name).MustBeSafeNormalizedText().MustBeWithinNormalizedLength(100);
RuleFor(c => c.Email).MustBeNormalizedEmailAddress();
```

`PlainText.Normalize` trims, applies Unicode normalisation and drops format characters: zero-width
spaces, the BOM, direction overrides. `<scr` + a zero-width space + `ipt>` holds no tag until something
strips that character, so a check on the raw value would pass it. `MustBeSafeText` refuses angle brackets
and control characters other than tab and line breaks; the length rule counts what will be stored.
An address is checked as an address: `javascript:alert(1)` holds no markup and is still refused by
`MustBeEmailAddress`.

## Pagination

A request that pages implements `IPaginationRequest` (`Page`, `PageSize`). Before the action, the page has
to be at least 1 and the page size between 1 and 1000, or the limit the request type declares:

```csharp
[PaginationLimit(200)]
public sealed record ListOrdersRequest(int Page, int PageSize, string? SortBy, SortDirection SortDir)
    : IPaginationRequest, ISortableRequest;
```

The OpenAPI document states the same bounds on the `page` and `pageSize` parameters. An action that takes a
type with these properties but does not page opts out with `[SkipPaginationValidation]`.

Sorting by a field the repository does not allow answers 400 with the list of allowed fields; see
[persistence](persistence.md).

## Why 422

Every failure of an input check answers 422, so a client has one status to handle for "fix your input".
400 is left for requests the code itself refuses with `BadRequestException`, such as sorting by a field
the endpoint does not offer.

## Tests

`Common.Tests` checks on a test server that a failing validator, out-of-bounds paging and a request-specific
limit answer 422 before the action runs, that field names follow the JSON, and that the OpenAPI bounds
match.

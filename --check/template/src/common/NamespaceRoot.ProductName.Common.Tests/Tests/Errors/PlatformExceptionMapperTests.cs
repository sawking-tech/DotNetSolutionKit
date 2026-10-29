using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ValidationFailure = FluentValidation.Results.ValidationFailure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using NamespaceRoot.ProductName.Common.Contracts.Responses;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Web.Errors;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Errors;

/// <summary>
/// What an exception becomes is the outward contract of every service: the status a client branches on
/// and the code its error handling matches. It is pinned here rather than discovered from a running
/// service.
/// </summary>
[TestFixture]
internal class PlatformExceptionMapperTests
{
    private static PlatformExceptionMapper Mapper(
        bool revealTechnicalDetail = false,
        params IExceptionMapping[] serviceMappings) =>
        new(serviceMappings, revealTechnicalDetail);

    private static object? Code(ProblemDetails problem) => problem.Extensions[PlatformExceptionMapper.CodeExtension];

    [Test]
    public void A_missing_entity_is_a_404()
    {
        var problem = Mapper().Map(new NotFoundException("Order not found"));

        problem.Status.ShouldBe(404);
        problem.Title.ShouldBe("Not Found");
        Code(problem).ShouldBe(ErrorConstants.Codes.NotFound);
        problem.Detail.ShouldBe("Order not found");
    }

    [Test]
    public void A_denied_action_is_a_403_and_not_the_422_of_its_base_class()
    {
        var problem = Mapper().Map(new AccessDeniedException("Not your order"));

        problem.Status.ShouldBe(403, "the derived case has to be matched before the base one");
        Code(problem).ShouldBe(ErrorConstants.Codes.Forbidden);
    }

    [Test]
    public void A_broken_business_rule_is_a_422_carrying_its_own_code()
    {
        var problem = Mapper().Map(new BusinessLogicException("Balance too low", "BALANCE_TOO_LOW"));

        problem.Status.ShouldBe(422);
        Code(problem).ShouldBe("BALANCE_TOO_LOW", "a rule states its own code when it has one");
    }

    [Test]
    public void A_conflict_is_a_409()
    {
        var problem = Mapper().Map(new ConflictException("Already exists"));

        problem.Status.ShouldBe(409);
        Code(problem).ShouldBe(ErrorConstants.Codes.Conflict);
    }

    [Test]
    public void An_unusable_request_is_a_400_carrying_its_own_code()
    {
        // What sorting by an unknown field raises. Without this arm it would fall through to the
        // catch-all and come back a 500, with the list of fields that would have worked hidden.
        var problem = Mapper().Map(
            new BadRequestException("Cannot sort by 'nope'. Available: name.", "INVALID_SORT_FIELD"));

        problem.Status.ShouldBe(400);
        Code(problem).ShouldBe("INVALID_SORT_FIELD");
        problem.Detail.ShouldNotBeNull().ShouldContain("Available: name");
    }

    [Test]
    public void A_unique_violation_is_a_409()
    {
        var problem = Mapper().Map(new UniqueViolationException("Login already taken"));

        problem.Status.ShouldBe(409);
        Code(problem).ShouldBe(ErrorConstants.Codes.Conflict);
    }

    [Test]
    public void A_replayed_request_is_a_409()
    {
        var problem = Mapper().Map(new RequestDuplicationException("Same idempotency key"));

        problem.Status.ShouldBe(409);
        Code(problem).ShouldBe(ErrorConstants.Codes.Conflict);
    }

    [Test]
    public void A_concurrent_edit_tells_the_caller_to_reload_rather_than_echoing_the_internal_message()
    {
        var problem = Mapper().Map(new ConcurrencyException("Row version 7 != 8"));

        problem.Status.ShouldBe(409);
        problem.Detail.ShouldBe(ErrorConstants.Messages.ConcurrencyConflict);
        problem.Detail.ShouldNotBeNull().ShouldNotContain("version 7");
    }

    [Test]
    public void Bad_input_names_the_parameter_that_was_wrong()
    {
        var problem = Mapper().Map(new InconsistentDataException("sku cannot contain empty values", "sku"));

        problem.Status.ShouldBe(400);
        problem.ShouldBeAssignableTo<HttpValidationProblemDetails>()!.Errors.Keys.ShouldContain("sku");
    }

    [Test]
    public void A_failed_validator_is_a_422_listing_every_failing_field()
    {
        var problem = Mapper().Map(new FluentValidation.ValidationException(
        [
            new ValidationFailure("Name", "Name is required"),
            new ValidationFailure("Name", "Name is too short"),
            new ValidationFailure("Email", "Email is invalid"),
        ]));

        problem.Status.ShouldBe(422);
        Code(problem).ShouldBe(ErrorConstants.Codes.ValidationError);
        var errors = problem.ShouldBeAssignableTo<HttpValidationProblemDetails>()!.Errors;
        errors["Name"].Length.ShouldBe(2);
        errors.Keys.ShouldContain("Email");
    }

    [Test]
    public void A_rejected_token_is_a_401()
    {
        var problem = Mapper().Map(new SecurityTokenExpiredException("Token expired"));

        problem.Status.ShouldBe(401);
        Code(problem).ShouldBe(ErrorConstants.Codes.InvalidToken);
    }

    [Test]
    public void A_rate_limit_is_a_429()
    {
        var problem = Mapper().Map(new RateLimitException(100, TimeSpan.FromMinutes(1)));

        problem.Status.ShouldBe(429);
        problem.Title.ShouldBe("Too Many Requests");
        Code(problem).ShouldBe(ErrorConstants.Codes.RateLimit);
    }

    [Test]
    public void An_unavailable_dependency_is_a_503()
    {
        var problem = Mapper().Map(new ServiceUnavailableException("The payment gateway is down"));

        problem.Status.ShouldBe(503);
        Code(problem).ShouldBe(ErrorConstants.Codes.ServiceUnavailable);
    }

    [Test]
    public void A_cancelled_request_is_not_counted_as_a_server_error()
    {
        var problem = Mapper().Map(new OperationCanceledException());

        problem.Status.ShouldBe(499, "a client that went away must not inflate the 5xx rate");
        problem.Title.ShouldBe("Client Closed Request");
        Code(problem).ShouldBe(ErrorConstants.Codes.RequestCancelled);
    }

    [Test]
    public void Malformed_json_is_a_400_rather_than_a_crash()
    {
        var problem = Mapper().Map(new JsonException("Unexpected token at line 3"));

        problem.Status.ShouldBe(400);
        problem.Detail.ShouldNotBeNull().ShouldContain(ErrorConstants.Messages.MalformedJson);
    }

    [Test]
    public void A_failed_annotation_check_is_a_422()
    {
        var problem = Mapper().Map(new ValidationException("Email is required"));

        problem.Status.ShouldBe(422);
        Code(problem).ShouldBe(ErrorConstants.Codes.ValidationError);
    }

    [Test]
    public void A_misconfigured_service_never_explains_itself_to_the_client()
    {
        var problem = Mapper(revealTechnicalDetail: true)
            .Map(new ConfigurationException("Secret store token missing for /orders/prod"));

        problem.Status.ShouldBe(500);
        problem.Detail.ShouldBe(ErrorConstants.Messages.InternalServerError);
        problem.Detail.ShouldNotBeNull().ShouldNotContain("token");
    }

    [Test]
    public void An_unexpected_failure_stays_silent_in_production()
    {
        var problem = Mapper().Map(new InvalidOperationException("Connection to 10.0.0.5 refused"));

        problem.Status.ShouldBe(500);
        problem.Detail.ShouldBe(ErrorConstants.Messages.InternalServerError);
        problem.Detail.ShouldNotBeNull().ShouldNotContain("10.0.0.5");
    }

    [Test]
    public void An_unexpected_failure_is_explained_outside_production()
    {
        var problem = Mapper(revealTechnicalDetail: true)
            .Map(new InvalidOperationException("Connection refused"));

        problem.Status.ShouldBe(500);
        problem.Detail.ShouldBe("Connection refused", "a developer needs the reason without reading logs");
    }

    [Test]
    public void A_service_rule_is_consulted_before_the_shared_ones()
    {
        var mapper = Mapper(serviceMappings: new StubMapping(
            typeof(NotFoundException), PlatformExceptionMapper.Problem(410, "Gone", "GONE")));

        var problem = mapper.Map(new NotFoundException("Order not found"));

        problem.Status.ShouldBe(410, "a service may override a default for its own domain");
    }

    [Test]
    public void A_service_rule_that_does_not_apply_leaves_the_shared_answer_alone()
    {
        var mapper = Mapper(serviceMappings: new StubMapping(
            typeof(RateLimitException), PlatformExceptionMapper.Problem(410, "Gone", "GONE")));

        var problem = mapper.Map(new NotFoundException("Order not found"));

        problem.Status.ShouldBe(404);
    }

    private sealed class StubMapping(Type handles, ProblemDetails problem) : IExceptionMapping
    {
        public ProblemDetails? TryMap(Exception exception) => exception.GetType() == handles ? problem : null;
    }
}

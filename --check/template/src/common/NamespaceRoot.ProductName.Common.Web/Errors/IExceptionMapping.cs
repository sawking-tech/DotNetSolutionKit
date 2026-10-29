using Microsoft.AspNetCore.Mvc;

namespace NamespaceRoot.ProductName.Common.Web.Errors;

/// <summary>
/// A service's own rule for turning an exception into a problem response.
/// </summary>
/// <remarks>
/// The shared mapper knows the exceptions the shared layers define. A service that throws an exception of
/// its own, or wants a different answer for a shared one, registers an implementation; registered rules
/// are consulted before the shared ones.
/// </remarks>
public interface IExceptionMapping
{
    /// <summary>
    /// Returns the problem for this exception, or <c>null</c> when the rule does not apply.
    /// </summary>
    ProblemDetails? TryMap(Exception exception);
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NamespaceRoot.ProductName.Common.Application.FeatureManagement;
using NamespaceRoot.ProductName.Common.FeatureManagement;

namespace NamespaceRoot.ProductName.Common.Web.FeatureManagement;

/// <summary>
/// The platform's feature flags: what exists, what is on, and where each value came from.
/// </summary>
/// <remarks>
/// <para>
/// Shipped with the platform rather than written per service, so every service answers the same way.
/// A service opts in with <c>AddApplicationPart</c> on this assembly.
/// </para>
/// <para>
/// Reading is anonymous. Nothing here is secret — a key, a value and a sentence about what it turns
/// on — and a client needs the list before anyone has signed in, to decide what to render on the way
/// to the login screen.
/// </para>
/// <para>
/// There is no write. A flag is switched by editing the configuration it comes from - <c>features.json</c>
/// on disk or the secret store - and services re-read it.
/// </para>
/// <para>
/// There is deliberately no per-key read. A client resolves flags synchronously while rendering, so
/// it holds the list it was given; a per-key endpoint would turn one page into a request per
/// conditional block. Inside a service the same question is a method on <c>IFeatureCatalog</c>.
/// </para>
/// <para>
/// What this endpoint is <em>not</em>: a means of enforcement. A client list decides what is drawn;
/// whether an operation is allowed is decided by the service performing it, which checks the flag
/// itself. Otherwise turning a feature off merely hides a button from whoever reloaded the page, and
/// leaves a stale mobile client untouched.
/// </para>
/// </remarks>
[ApiController]
[Route("api/v1/features")]
[Tags("Platform — Features")]
public sealed class FeaturesController(IFeatureCatalog catalog) : ControllerBase
{
    /// <summary>Every declared feature with its current value, source and expiry.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(FeaturesResponse), StatusCodes.Status200OK)]
    public ActionResult<FeaturesResponse> Get() =>
        Ok(new FeaturesResponse { Features = catalog.GetAll() });

}

/// <summary>Body of <c>GET /api/v1/features</c>.</summary>
public sealed record FeaturesResponse
{
    /// <summary>
    /// Declared features, ordered by key. An empty list means this platform declares no optional
    /// features — not that the call failed.
    /// </summary>
    public required IReadOnlyList<FeatureState> Features { get; init; }
}

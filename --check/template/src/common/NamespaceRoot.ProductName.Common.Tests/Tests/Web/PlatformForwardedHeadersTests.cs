using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using NamespaceRoot.ProductName.Common.Web.Setup;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Web;

/// <summary>
/// The client address, scheme, host and path base behind a proxy: taken from the forwarded headers, and
/// only from the listed proxies once a list is configured. The host and the path base are what a service
/// builds its links from.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class PlatformForwardedHeadersTests
{
    private static async Task<string> Send(string connectedFrom, params (string Key, string Value)[] settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value));
        builder.Services.AddPlatformForwardedHeaders(builder.Configuration);
        await using var app = builder.Build();

        // The address the request arrives from, as a proxy's would be.
        app.Use((context, next) =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(connectedFrom);
            return next(context);
        });
        app.UsePlatformForwardedHeaders();
        app.Run(context => context.Response.WriteAsync(
            $"{context.Request.Scheme} {context.Connection.RemoteIpAddress} {context.Request.Host}{context.Request.PathBase}"));
        await app.StartAsync();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.7");
        request.Headers.Add("X-Forwarded-Host", "shop.example");
        request.Headers.Add("X-Forwarded-Prefix", "/shop");
        return await (await app.GetTestClient().SendAsync(request)).Content.ReadAsStringAsync();
    }

    [Test(Description = "By default the client address, scheme, host and path base come from the proxy's headers")]
    public async Task Should_UseTheForwardedValues_ByDefault() =>
        (await Send("172.18.0.5")).ShouldBe("https 203.0.113.7 shop.example/shop");

    [Test(Description = "A listed proxy is trusted")]
    public async Task Should_UseTheForwardedValues_When_TheProxyIsListed() =>
        (await Send("10.0.0.2", ("ForwardedHeaders:KnownProxies:0", "10.0.0.2"))).ShouldBe("https 203.0.113.7 shop.example/shop");

    [Test(Description = "A proxy inside a listed network is trusted")]
    public async Task Should_UseTheForwardedValues_When_TheProxyIsInAListedNetwork() =>
        (await Send("10.1.2.3", ("ForwardedHeaders:KnownNetworks:0", "10.1.0.0/16"))).ShouldBe("https 203.0.113.7 shop.example/shop");

    [Test(Description = "Once proxies are listed, headers from any other address are ignored")]
    public async Task Should_IgnoreTheHeaders_When_TheSenderIsNotAListedProxy() =>
        (await Send("198.51.100.9", ("ForwardedHeaders:KnownProxies:0", "10.0.0.2"))).ShouldBe("http 198.51.100.9 localhost");

    [Test(Description = "A forwarded host among the allowed ones is taken")]
    public async Task Should_UseTheForwardedHost_When_ItIsAllowed() =>
        (await Send("172.18.0.5", ("ForwardedHeaders:AllowedHosts:0", "*.example"))).ShouldBe("https 203.0.113.7 shop.example/shop");

    [Test(Description = "A forwarded host outside the allowed ones is not taken; the request keeps its own Host, the other headers still apply")]
    public async Task Should_IgnoreTheForwardedHost_When_ItIsNotAllowed() =>
        (await Send("172.18.0.5", ("ForwardedHeaders:AllowedHosts:0", "api.example.com"))).ShouldBe("https 203.0.113.7 localhost/shop");
}

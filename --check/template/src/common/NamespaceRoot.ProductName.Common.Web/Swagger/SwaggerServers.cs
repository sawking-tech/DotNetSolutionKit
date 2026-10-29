using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace NamespaceRoot.ProductName.Common.Web.Swagger;

/// <summary>
/// Puts <see cref="ISwaggerSettings.PublicServers"/> into the generated documents. An
/// <see cref="IConfigureOptions{TOptions}"/>, so the settings arrive validated through the container
/// rather than read from the configuration while it is being built.
/// </summary>
internal sealed class ConfigureSwaggerServers(ISwaggerSettings settings) : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options)
    {
        foreach (var server in settings.PublicServers)
            options.AddServer(new OpenApiServer { Url = server.Url, Description = server.Description });
    }
}

/// <summary>
/// The same servers as JSON, for a document that does not pass through Swashbuckle: the gateway's copy of
/// a service's document.
/// </summary>
public static class SwaggerServers
{
    /// <summary>The <c>servers</c> array, or null when none is configured.</summary>
    public static JsonArray? ToJson(ISwaggerSettings settings)
    {
        if (settings.PublicServers.Count == 0)
            return null;

        var servers = new JsonArray();
        foreach (var server in settings.PublicServers)
        {
            var node = new JsonObject { ["url"] = server.Url };
            if (!string.IsNullOrWhiteSpace(server.Description))
                node["description"] = server.Description;
            servers.Add(node);
        }

        return servers;
    }
}

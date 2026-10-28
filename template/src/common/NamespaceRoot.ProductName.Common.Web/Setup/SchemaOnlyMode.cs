// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace NamespaceRoot.ProductName.Common.Web.Setup;

/// <summary>
/// Starts a service for its metadata only - no database, no broker, no background jobs.
/// </summary>
/// <remarks>
/// Generating an OpenAPI document means building the host: the document comes from ApiExplorer,
/// which only exists once routing, conventions and the operation filters are wired up. Building the
/// host, however, also runs the migration and seeding block in <c>Program.cs</c>, so asking a
/// service what its API looks like used to migrate a database as a side effect.
/// <para>
/// This mode answers that by layering "off" over every infrastructure switch the services already
/// read, so no service code has to learn about it. Enable it with the <c>--schema-only</c> argument
/// or <c>SCHEMA_ONLY=1</c>.
/// </para>
/// <para>
/// One thing the switches cannot cover: authentication has to be left out of the container as well.
/// Its handlers resolve repositories from the infrastructure layer, and <c>WebApplication</c> adds the
/// authentication middleware on its own as soon as it finds the schemes registered - so not calling
/// <c>UseAppAuthentication</c> changes nothing, and every request fails on a handler it cannot build.
/// Each service therefore registers authentication and authorisation only when this mode is off.
/// </para>
/// </remarks>
public static class SchemaOnlyMode
{
    public const string Switch = "--schema-only";
    public const string EnvironmentVariable = "SCHEMA_ONLY";

    /// <summary>Infrastructure switches the services consult before touching anything external.</summary>
    private static readonly string[] InfrastructureSwitches =
    [
        "Database:Enabled",
        "RabbitMq:Enabled",
        "HangfireSettings:Enabled",
        // A switch the service adds for its own infrastructure (object storage, a search index)
        // belongs here too, so a schema-only run leaves it alone as well.
    ];

    /// <summary>Whether this process was started to produce metadata rather than to serve traffic.</summary>
    public static bool IsEnabled(string[]? args = null)
    {
        if (args is not null && args.Any(a => string.Equals(a, Switch, StringComparison.OrdinalIgnoreCase)))
            return true;

        var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return value is not null
               && (value.Equals("1", StringComparison.Ordinal)
                   || value.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Turns every infrastructure switch off when the mode is on; a no-op otherwise.
    /// Add it last so it wins over the files and the environment.
    /// </summary>
    public static IConfigurationBuilder AddSchemaOnlyOverrides(this IConfigurationBuilder builder, string[]? args = null)
    {
        if (!IsEnabled(args)) return builder;

        builder.AddInMemoryCollection(
            InfrastructureSwitches.ToDictionary(key => key, _ => (string?)"false"));

        return builder;
    }

    /// <summary>
    /// Drops the startup validation of options when the mode is on; a no-op otherwise.
    /// </summary>
    /// <remarks>
    /// Settings marked <c>ValidateOnStart</c> are checked before the host serves anything, and the
    /// required ones - API keys, secrets, public URLs - live in secret stores that a build agent has
    /// no reason to hold. A schema-only run answers one question about routes and shapes and serves
    /// no traffic, so nothing it reports depends on those values being real. Filling them with
    /// placeholders instead would mean maintaining a list that grows with every new setting.
    /// </remarks>
    public static IServiceCollection RemoveStartupValidation(this IServiceCollection services, string[]? args = null)
    {
        if (!IsEnabled(args)) return services;

        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IStartupValidator)).ToList())
            services.Remove(descriptor);

        return services;
    }
}

// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using System.Reflection;
using NamespaceRoot.ProductName.Common.Application.Configuration;
using NamespaceRoot.ProductName.Common.Web.Authentication;
using NamespaceRoot.ProductName.Common.Web.Gateway;
using NamespaceRoot.ProductName.Common.Web.Setup;
using NamespaceRoot.ProductName.ServiceNameOrCustom.API.Setup;
using Serilog;

// The gateway: the one public entry point. It validates the caller's token, routes the request to a
// service (ReverseProxy in appsettings.json) and tells the service who the caller is - headers the
// service trusts because the internal API key comes with them.
try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddPlatformLogging();
    builder.Host.UseDefaultServiceProvider((_, options) =>
    {
        options.ValidateScopes = true;
        options.ValidateOnBuild = true;
    });

    // Errors, JSON, CORS, Swagger and health, the same as every service (Common.Web).
    builder.AddPlatformWebApi(typeof(Program).Assembly);

    builder.Services.AddInternalApiConfiguration(builder.Configuration);
    builder.SetupGatewayAuthentication();
    builder.Services.AddAuthorization();

    // The services' Swagger documents on the gateway's page, listed by their /swagger/<cluster>/ routes and
    // cut to the paths the gateway's routes reach.
    builder.Services.AddGatewaySwagger(builder.Configuration.GetSection("ReverseProxy"));

    builder.Services.AddReverseProxy()
        .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
        .AddGatewayTransforms();

    var app = builder.Build();

    // A token that failed validation stops here; no token goes on, and the service decides whether
    // its endpoint is public.
    app.UsePlatformPipeline(typeof(Program).Assembly, beforeEndpoints: pipeline => pipeline.UseRejectInvalidCredentials());
    app.MapGatewaySwagger(builder.Configuration.GetSection("ReverseProxy"));
    app.MapReverseProxy();

    app.Run();
}
catch (HostAbortedException ex)
{
    Log.Warning(ex, "Host was aborted. This may be expected in some environments.");
}
catch (Exception ex) when (ex.GetType().Name == "StopTheHostException")
{
    throw;
}
catch (Exception ex)
{
    Log.Fatal(ex, "The {EntryAssemblyName} application startup failed", Assembly.GetEntryAssembly()?.GetName().Name);
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}
finally
{
    Log.CloseAndFlush();
}

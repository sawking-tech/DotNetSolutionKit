// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Writers;
using Swashbuckle.AspNetCore.Swagger;

namespace NamespaceRoot.ProductName.Common.Web.Setup;

/// <summary>
/// Writes the service's own OpenAPI document to a file and ends the run.
/// </summary>
/// <remarks>
/// The document is read straight out of the built application, so producing it needs no port, no
/// readiness wait and no process to kill - the parts that used to fail, and once quietly served one
/// service's contract under another's name.
/// <para>
/// What comes out is the service in its own terms, with a title naming the environment. Making it
/// stable across machines (titles, line endings, ordering) is done outside - see
/// <c>scripts/normalise_api_schema.py</c>.
/// </para>
/// </remarks>
public static class SchemaDump
{
    public const string Switch = "--dump-schema";
    public const string DocumentSwitch = "--document";

    /// <summary>
    /// Writes the requested document when <c>--dump-schema</c> was passed; reports whether it did, so
    /// the caller knows to stop instead of serving traffic.
    /// </summary>
    public static bool TryWrite(WebApplication app, string[] args)
    {
        var destination = ValueOf(args, Switch);
        if (destination is null) return false;

        var documentName = ValueOf(args, DocumentSwitch)
            ?? throw new ArgumentException($"{Switch} needs {DocumentSwitch} to say which document to write.");

        var document = app.Services.GetRequiredService<ISwaggerProvider>().GetSwagger(documentName);

        var text = new StringBuilder();
        using (var writer = new StringWriter(text))
            document.SerializeAsV3(new OpenApiJsonWriter(writer));

        var full = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text.ToString());

        Console.WriteLine($"    {document.Paths.Count} paths -> {destination}");
        return true;
    }

    private static string? ValueOf(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}

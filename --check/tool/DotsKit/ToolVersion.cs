using System.Reflection;

namespace DotsKit;

internal static class ToolVersion
{
    /// <summary>The tool's version, which is the template's it is released with: 2.8.0, without the build's +sha.</summary>
    public static string Current { get; } =
        (typeof(ToolVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];
}

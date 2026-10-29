using System.Reflection;
using System.Text.Json;
using NamespaceRoot.ProductName.Common.FeatureManagement;
using NUnit.Framework;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.FeatureManagement;

/// <summary>
/// Keeps the constants and the file honest with each other.
/// </summary>
/// <remarks>
/// Flags live in the file, so code that reads one refers to it by a constant — that is what gives
/// autocomplete and makes a rename a refactor. The two can drift: a constant renamed without the
/// file, a flag deleted while code still asks for it. Both are silent at runtime, because an unknown
/// key simply answers false, and this is the cheapest place to catch them.
/// </remarks>
[TestFixture]
public class FeatureKeysTests
{
    private static IReadOnlyList<string> DeclaredKeys()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "features.json");
        File.Exists(path).ShouldBeTrue(
            $"'{path}' should have been copied beside the tests — it ships from Common so every " +
            "service reads the same flags, and its absence means that copy is broken.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.GetProperty("Features")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToList();
    }

    private static IReadOnlyList<string> Constants() =>
        typeof(FeatureKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

    [Test(Description = "Every constant names a flag that actually exists")]
    public void Should_DeclareEveryConstantInTheFile()
    {
        var declared = DeclaredKeys();

        foreach (var key in Constants())
        {
            declared.ShouldContain(key,
                $"FeatureKeys names '{key}', but features.json does not declare it. Code asking for it " +
                "would get false and never say why.");
        }
    }

    [Test(Description = "Flags are declared in lower kebab-case, dotted for sub-features")]
    public void Should_KeepKeysToTheAgreedShape()
    {
        foreach (var key in DeclaredKeys())
        {
            key.ShouldBe(key.ToLowerInvariant(), $"'{key}' should be lower case");
            key.ShouldNotContain(" ", Case.Sensitive, $"'{key}' should not contain spaces");
            key.ShouldNotContain("_", Case.Sensitive, $"'{key}' should use '-' rather than '_'");
        }
    }
}

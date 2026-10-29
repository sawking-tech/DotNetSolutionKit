//#if (TestFramework == "xunit")
global using Xunit;
//#else
global using NUnit.Framework;
//#endif
global using Shouldly;
using System.Runtime.CompilerServices;
using NamespaceRoot.ProductName.Common.Tests;
//#if (TestFramework == "xunit")
using Xunit.v3;
//#endif

//#if (TestFramework == "nunit")
// A fixture is built for each test, as xUnit does: a test sets itself up in the constructor and cleans up
// in Dispose, whatever runs it.
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
//#endif

namespace NamespaceRoot.ProductName.ServiceNameOrCustom.Tests;

// The one place that knows the test framework. The tests carry these attributes and nothing of the
// framework's own, so a test reads the same under NUnit and xUnit and a change of framework touches only
// this file:
//
//   [Test(Description = "...")]  a test
//   [TestOf(typeof(...))]        the class a fixture tests, so a search for the class finds its tests
//   [Integration]                needs a real database: CI runs it apart, `TestCategory=Integration`
//   [RunsInParallel]             the fixture runs in parallel with others; under NUnit its tests do too,
//                                under xUnit they run one after another
//   [RunsAlone]                  the fixture does not run in parallel with others

//#if (TestFramework == "xunit")
/// <summary>A test. <see cref="Description"/> is what a runner shows for it.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute(
    [CallerFilePath] string? sourceFilePath = null,
    [CallerLineNumber] int sourceLineNumber = -1) : FactAttribute(sourceFilePath, sourceLineNumber)
{
    public string? Description
    {
        get => DisplayName;
        set => DisplayName = value;
    }
}

/// <summary>The class a fixture tests.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TestOfAttribute(Type type) : Attribute, ITraitAttribute
{
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() => [new("TestOf", type.Name)];
}

/// <summary>A test or fixture that needs a real database.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IntegrationAttribute : Attribute, ITraitAttribute
{
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [new(TestCategories.TraitName, TestCategories.Integration)];
}

/// <summary>
/// A fixture whose tests run in parallel. xUnit already runs fixtures in parallel, so the attribute only
/// says so; the tests of one fixture run one after another.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RunsInParallelAttribute : Attribute;

/// <summary>A fixture that does not run in parallel with others.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RunsAloneAttribute : Attribute, ICollectionAttribute
{
    public string Name => nameof(RunsAloneCollection);

    public Type? Type => null;
}

/// <summary>The collection <see cref="RunsAloneAttribute"/> puts a fixture in.</summary>
[CollectionDefinition(nameof(RunsAloneCollection), DisableParallelization = true)]
public sealed class RunsAloneCollection;

/// <summary>Tells the shared test infrastructure that a skip here is xUnit's skip.</summary>
internal static class TestSkipSetup
{
    [ModuleInitializer]
    internal static void UseXunit() => TestSkip.Handler = reason => Assert.Skip(reason);
}
//#else
/// <summary>A test or fixture that needs a real database.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IntegrationAttribute() : CategoryAttribute(TestCategories.Integration);

/// <summary>A fixture whose tests run in parallel with each other and with other fixtures.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RunsInParallelAttribute() : ParallelizableAttribute(ParallelScope.All);

/// <summary>A fixture that does not run in parallel with others.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RunsAloneAttribute() : ParallelizableAttribute(ParallelScope.None);

/// <summary>Tells the shared test infrastructure that a skip here is NUnit's ignore.</summary>
internal static class TestSkipSetup
{
    [ModuleInitializer]
    internal static void UseNUnit() => TestSkip.Handler = Assert.Ignore;
}
//#endif

using DotsKit.Solutions;

namespace DotsKit.Tests;

/// <summary>The full names of a type the template moved, found by the C# syntax and renamed in place.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class QualifiedNamesTests
{
    private const string Old = "Acme.Shop.Common.Events";
    private const string New = "Acme.Shop.Common.Domain.Events";

    private const string Code = """
        using Acme.Shop.Common.Events;
        using System.Collections.Generic;

        namespace Acme.Shop.Common.Events.Team;

        // Acme.Shop.Common.Events.DomainEvent in a comment stays.
        [System.Serializable]
        public sealed class Order
        {
            public Acme.Shop.Common.Events.DomainEvent? Last { get; set; }
            public List<Acme.Shop.Common.Events.DomainEvent> All { get; } = new();
            public global::Acme.Shop.Common.Events.DomainEvent First() => new Acme.Shop.Common.Events.DomainEvent();
            public System.Type Kind => typeof(Acme.Shop.Common.Events.DomainEvent);
            public object Made => Acme.Shop.Common.Events.DomainEvent.Create();
            public Acme.Shop.Common.Events.OrderShipped? Shipped { get; set; }
            public string Text => "Acme.Shop.Common.Events.DomainEvent";
        }
        """;

    [Test]
    public void EveryFullNameOfTheType_NamesItsNewNamespace_AndNothingElseChanges()
    {
        var replaced = QualifiedNames.Replace(Code, Old, "DomainEvent", New);

        replaced.ShouldBe(Code
            .Replace("public Acme.Shop.Common.Events.DomainEvent?", $"public {New}.DomainEvent?", StringComparison.Ordinal)
            .Replace("List<Acme.Shop.Common.Events.DomainEvent>", $"List<{New}.DomainEvent>", StringComparison.Ordinal)
            .Replace("global::Acme.Shop.Common.Events.DomainEvent", $"global::{New}.DomainEvent", StringComparison.Ordinal)
            .Replace("new Acme.Shop.Common.Events.DomainEvent()", $"new {New}.DomainEvent()", StringComparison.Ordinal)
            .Replace("typeof(Acme.Shop.Common.Events.DomainEvent)", $"typeof({New}.DomainEvent)", StringComparison.Ordinal)
            .Replace("=> Acme.Shop.Common.Events.DomainEvent.Create()", $"=> {New}.DomainEvent.Create()", StringComparison.Ordinal));
        replaced.ShouldContain("using Acme.Shop.Common.Events;");
        replaced.ShouldContain("namespace Acme.Shop.Common.Events.Team;");
        replaced.ShouldContain("// Acme.Shop.Common.Events.DomainEvent in a comment stays.");
        replaced.ShouldContain("Acme.Shop.Common.Events.OrderShipped?");
        replaced.ShouldContain("\"Acme.Shop.Common.Events.DomainEvent\"");
    }

    [Test]
    public void AnAliasAndAStaticUsing_NameTheType_AndAreRepaired_APlainUsingIsNot()
    {
        const string code = "using Acme.Shop.Common.Events;\nusing E = Acme.Shop.Common.Events.DomainEvent;\nusing static Acme.Shop.Common.Events.DomainEvent;\n";

        QualifiedNames.Replace(code, Old, "DomainEvent", New)
            .ShouldBe($"using Acme.Shop.Common.Events;\nusing E = {New}.DomainEvent;\nusing static {New}.DomainEvent;\n");
    }

    [Test]
    public void AFileThatNamesTheTypeOnlyInAUsing_ACommentOrAString_NamesNothing()
    {
        QualifiedNames.Names(Code, Old, "DomainEvent").ShouldBeTrue();
        QualifiedNames.Names("using Acme.Shop.Common.Events.DomainEvent;\n// Acme.Shop.Common.Events.DomainEvent\nclass A { string s = \"Acme.Shop.Common.Events.DomainEvent\"; }\n",
            Old, "DomainEvent").ShouldBeFalse();
        QualifiedNames.Names(Code, Old, "OrderPlaced").ShouldBeFalse();
    }
}

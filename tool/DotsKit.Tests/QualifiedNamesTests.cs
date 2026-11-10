using DotsKit.Solutions;

namespace DotsKit.Tests;

/// <summary>The full names of a type the template moved, found by the C# syntax and renamed in place.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class QualifiedNamesTests
{
    private const string Old = "ST.Demo.Common.Events";
    private const string New = "ST.Demo.Common.Domain.Events";

    private const string Code = """
        using ST.Demo.Common.Events;
        using System.Collections.Generic;

        namespace ST.Demo.Common.Events.Team;

        // ST.Demo.Common.Events.DomainEvent in a comment stays.
        [System.Serializable]
        public sealed class Order
        {
            public ST.Demo.Common.Events.DomainEvent? Last { get; set; }
            public List<ST.Demo.Common.Events.DomainEvent> All { get; } = new();
            public global::ST.Demo.Common.Events.DomainEvent First() => new ST.Demo.Common.Events.DomainEvent();
            public System.Type Kind => typeof(ST.Demo.Common.Events.DomainEvent);
            public object Made => ST.Demo.Common.Events.DomainEvent.Create();
            public ST.Demo.Common.Events.OrderShipped? Shipped { get; set; }
            public string Text => "ST.Demo.Common.Events.DomainEvent";
        }
        """;

    [Test]
    public void EveryFullNameOfTheType_NamesItsNewNamespace_AndNothingElseChanges()
    {
        var replaced = QualifiedNames.Replace(Code, Old, "DomainEvent", New);

        replaced.ShouldBe(Code
            .Replace("public ST.Demo.Common.Events.DomainEvent?", $"public {New}.DomainEvent?", StringComparison.Ordinal)
            .Replace("List<ST.Demo.Common.Events.DomainEvent>", $"List<{New}.DomainEvent>", StringComparison.Ordinal)
            .Replace("global::ST.Demo.Common.Events.DomainEvent", $"global::{New}.DomainEvent", StringComparison.Ordinal)
            .Replace("new ST.Demo.Common.Events.DomainEvent()", $"new {New}.DomainEvent()", StringComparison.Ordinal)
            .Replace("typeof(ST.Demo.Common.Events.DomainEvent)", $"typeof({New}.DomainEvent)", StringComparison.Ordinal)
            .Replace("=> ST.Demo.Common.Events.DomainEvent.Create()", $"=> {New}.DomainEvent.Create()", StringComparison.Ordinal));
        replaced.ShouldContain("using ST.Demo.Common.Events;");
        replaced.ShouldContain("namespace ST.Demo.Common.Events.Team;");
        replaced.ShouldContain("// ST.Demo.Common.Events.DomainEvent in a comment stays.");
        replaced.ShouldContain("ST.Demo.Common.Events.OrderShipped?");
        replaced.ShouldContain("\"ST.Demo.Common.Events.DomainEvent\"");
    }

    [Test]
    public void AnAliasAndAStaticUsing_NameTheType_AndAreRepaired_APlainUsingIsNot()
    {
        const string code = "using ST.Demo.Common.Events;\nusing E = ST.Demo.Common.Events.DomainEvent;\nusing static ST.Demo.Common.Events.DomainEvent;\n";

        QualifiedNames.Replace(code, Old, "DomainEvent", New)
            .ShouldBe($"using ST.Demo.Common.Events;\nusing E = {New}.DomainEvent;\nusing static {New}.DomainEvent;\n");
    }

    [Test]
    public void AFileThatNamesTheTypeOnlyInAUsing_ACommentOrAString_NamesNothing()
    {
        QualifiedNames.Names(Code, Old, "DomainEvent").ShouldBeTrue();
        QualifiedNames.Names("using ST.Demo.Common.Events.DomainEvent;\n// ST.Demo.Common.Events.DomainEvent\nclass A { string s = \"ST.Demo.Common.Events.DomainEvent\"; }\n",
            Old, "DomainEvent").ShouldBeFalse();
        QualifiedNames.Names(Code, Old, "OrderPlaced").ShouldBeFalse();
    }
}


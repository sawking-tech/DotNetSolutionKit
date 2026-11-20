using DotsKit.Ignoring;

namespace DotsKit.Tests;

/// <summary>
/// The rules of <c>.dotskitignore</c>: how a line is read, what a pin holds, and what the tool writes back once a pin is
/// spent. No file and no git: the decisions, not the matching git does.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class DotskitIgnoreTests
{
    private static IReadOnlyList<IgnoreRule> Rules(string text) => DotskitIgnore.Parse(text).Rules;

    [Test]
    public void ALine_IsAPatternWithAnOptionalPin_AndABlankOrACommentIsNoRule()
    {
        var rules = Rules("""
            # what the upgrade does not touch
            src/common/ST.Demo.Common.Infrastructure/Persistence/Locked.cs
            src/services/ST.Demo.Orders/ST.Demo.Orders.Infrastructure/**@2.9

            src/capabilities/@major
            """);

        rules.Select(r => (r.Pattern, Pin: r.Pin?.PinText())).ShouldBe([
            ("src/common/ST.Demo.Common.Infrastructure/Persistence/Locked.cs", null),
            ("src/services/ST.Demo.Orders/ST.Demo.Orders.Infrastructure/**", "2.9"),
            ("src/capabilities/", "major"),
        ]);
    }

    [Test]
    public void ATheLastAt_SeparatesThePin_AndASuffixThatIsNoPinLeavesThePathWhole()
    {
        Rules("a/b@2.9").Single().Pattern.ShouldBe("a/b");
        Rules("a/b@2.9").Single().Pin!.Major.ShouldBe(2);
        // A path that carries an @ of its own and no pin keeps it.
        Rules("src/team@example.com/rules.json").Single().ShouldBe(new IgnoreRule("src/team@example.com/rules.json", null, 1));
        Rules("src/team@example.com/rules.json@2").Single().Pattern.ShouldBe("src/team@example.com/rules.json");
    }

    [Test]
    public void ACancellingRule_IsARuleOfItsOwn_AndMayBePinnedToo()
    {
        var rule = Rules("!src/common/keep.cs@2.9").Single();

        rule.Pattern.ShouldBe("!src/common/keep.cs");
        rule.Pin!.PinText().ShouldBe("2.9");
    }

    [TestCase("2.9", "2.9.0", true)]
    [TestCase("2.9", "2.9.4", true)]
    [TestCase("2.9", "2.10.0", false)]
    [TestCase("2.9", "3.0.0", false)]
    [TestCase("2", "2.9.9", true)]
    [TestCase("2", "3.0.0", false)]
    [TestCase("major", "2.9.0", true)]
    [TestCase("major", "9.0.0", true)]
    public void APin_HoldsWithinItsLine_AndIsSpentPastIt(string pin, string version, bool holds)
    {
        Pin.Parse(pin)!.Holds(version).ShouldBe(holds);
    }

    [TestCase("2.9", "3.1.4", "3.1")]
    [TestCase("2", "3.1.4", "3")]
    [TestCase("major", "3.1.4", "major")]
    public void ARenewedPin_KeepsItsPrecision_OnTheLineOfTheVersionItGoesTo(string pin, string version, string renewed)
    {
        Pin.Parse(pin)!.RenewedTo(version).ShouldBe(renewed);
    }

    [Test]
    public void ASpentPin_WhoseFilesAreTheTemplatesOwn_IsCommentedOut_NotErased()
    {
        var file = DotskitIgnore.Parse("""
            # The team's own rules
            src/locked/@2.9
            """);

        var (text, lines) = PinReview.Of(file, [(file.Rules[0], false)], "3.0.0");

        text.ShouldContain("# The team's own rules");
        text.ShouldNotContain("\nsrc/locked/@2.9\n");
        text.ShouldContain("# src/locked/@3.0 #");
        lines.ShouldHaveSingleItem().ShouldContain("commented out");
    }

    [Test]
    public void ASpentPin_WhoseFilesStillDiffer_IsRenewedToTheVersionTheUpgradeWentTo()
    {
        var file = DotskitIgnore.Parse("src/locked/@2.9");

        var (text, lines) = PinReview.Of(file, [(file.Rules[0], true)], "3.1.4");

        text.ShouldBe("src/locked/@3.1");
        lines.ShouldHaveSingleItem().ShouldContain("pinned on @3.1 again");
    }

    [Test]
    public void AFileWithNoSpentPin_IsWrittenAsItWas()
    {
        const string written = "# keep\nsrc/locked/@2.9\n";

        PinReview.Of(DotskitIgnore.Parse(written), [], "3.0.0").Text.ShouldBe(written.ReplaceLineEndings("\n"));
    }

    [Test]
    public void NoFile_MeansNoRules()
    {
        DotskitIgnore.Parse(null).IsEmpty.ShouldBeTrue();
        DotskitIgnore.Parse("").IsEmpty.ShouldBeTrue();
        DotskitIgnore.Parse(" # a comment only\n\n").IsEmpty.ShouldBeTrue();
    }
}

internal static class PinTestText
{
    /// <summary>The pin in the words the file writes it.</summary>
    public static string PinText(this Pin pin) => pin.EveryMajor ? "major" : pin.Minor is { } minor ? $"{pin.Major}.{minor}" : pin.Major.ToString();
}

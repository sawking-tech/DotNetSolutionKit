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
    private static IReadOnlyList<IgnoreRule> Rules(string text) => DotskitIgnore.Parse("", text).Rules;

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
        Rules("src/team@example.com/rules.json").Single().ShouldBe(new IgnoreRule("src/team@example.com/rules.json", null, "", 1));
        Rules("src/team@example.com/rules.json@2").Single().Pattern.ShouldBe("src/team@example.com/rules.json");
    }

    [Test]
    public void ACancellingRule_IsARuleOfItsOwn_AndMayBePinnedToo()
    {
        var rule = Rules("!src/common/keep.cs@2.9").Single();

        rule.Pattern.ShouldBe("!src/common/keep.cs");
        rule.Pin!.PinText().ShouldBe("2.9");
    }

    [Test]
    public void ARuleOfADeeperFile_CarriesTheFolderItsFileSitsIn_AndNamesItsFileForTheReview()
    {
        var rule = DotskitIgnore.Parse("src/common/X", "Locked.cs\n").Rules.Single();

        rule.Source.ShouldBe("src/common/X");
        rule.FilePath.ShouldBe("src/common/X/.dotskitignore");
        DotskitIgnore.Parse("", "Locked.cs\n").Rules.Single().FilePath.ShouldBe(".dotskitignore");
    }

    [TestCase("2.9", "2.9.0", true)]
    [TestCase("2.9", "2.9.4", true)]
    [TestCase("2.9", "2.10.0", false)]
    [TestCase("2.9", "3.0.0", false)]
    [TestCase("2", "2.9.9", true)]
    [TestCase("2", "3.0.0", false)]
    public void APin_HoldsWithinItsLine_AndIsSpentPastIt(string pin, string version, bool holds)
    {
        Pin.Parse(pin)!.Holds(version).ShouldBe(holds);
    }

    [TestCase("2.9.0", "3.0.0", false, "a pin of the major is spent by the next major")]
    [TestCase("2.9.4", "2.10.0", true, "it holds through the whole of its major")]
    [TestCase("2.9.0", "source", false, "the sources are the tool's line, which is the next major")]
    public void APinOfTheMajor_HoldsTheMajorTheSolutionIsOn_AndIsSpentByTheNext(string from, string to, bool holds, string why)
    {
        Pin.Parse("major")!.Holds(to, from).ShouldBe(holds, why);
    }

    [TestCase("2.9", "3.1.4", "3.1")]
    [TestCase("2", "3.1.4", "3")]
    [TestCase("major", "3.1.4", "3")]
    public void ARenewedPin_KeepsItsPrecision_OnTheLineOfTheVersionItGoesTo(string pin, string version, string renewed)
    {
        Pin.Parse(pin)!.RenewedTo(version).ShouldBe(renewed);
    }

    [Test]
    public void ASpentPin_WhoseFilesAreTheTemplatesOwn_IsCommentedOut_NotErased()
    {
        var file = DotskitIgnore.Parse("", """
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
        var file = DotskitIgnore.Parse("", "src/locked/@2.9");

        var (text, lines) = PinReview.Of(file, [(file.Rules[0], true)], "3.1.4");

        text.ShouldBe("src/locked/@3.1");
        lines.ShouldHaveSingleItem().ShouldContain("pinned on @3.1 again");
    }

    [Test]
    public void ASpentPinOfTheMajor_IsMovedOntoTheNextLine_CommentedOutForTheTeam()
    {
        var file = DotskitIgnore.Parse("", "src/locked/@major");

        var (text, lines) = PinReview.Of(file, [(file.Rules[0], false)], "3.0.0");

        // ADR-009: the old line is commented out and the next one is written commented out of its own, on the line the
        // solution has moved to, so the team uncomments a pin of the major it now holds.
        text.ShouldBe("# src/locked/@3 # dotskit: spent by the upgrade to 3.0.0; uncomment the line below to hold again\n# src/locked/@3");
        lines.ShouldHaveSingleItem().ShouldContain("written below it");
    }

    [Test]
    public void AFileWithNoSpentPin_IsWrittenAsItWas()
    {
        const string written = "# keep\nsrc/locked/@2.9\n";

        PinReview.Of(DotskitIgnore.Parse("", written), [], "3.0.0").Text.ShouldBe(written.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// A file takes only its own share of the spent pins: the pins of a rule of another folder are written by the file
    /// that rule sits in, and a file the tool rewrote with a stranger's rule would take the team's line for its own.
    /// </summary>
    [Test]
    public void AFileWritesItsOwnSpentPins_AndLeavesThoseOfAnotherFile()
    {
        var root = DotskitIgnore.Parse("", "src/locked/@2.9\n");
        var deeper = DotskitIgnore.Parse("src/common/X", "Other/@major\n");

        var (text, lines) = PinReview.Of(root, [(deeper.Rules[0], false)], "3.0.0");

        text.ShouldBe("src/locked/@2.9\n");
        lines.ShouldBeEmpty();
    }

    /// <summary>The report names the file and the line a person opens, not a number that means nothing without one.</summary>
    [Test]
    public void TheReview_NamesTheFileOfTheRuleAndItsLine()
    {
        var file = DotskitIgnore.Parse("src/common/X", "# ours\n@2.9\n".Replace("@2.9", "Locked.cs@2.9"));

        var (_, lines) = PinReview.Of(file, [(file.Rules[0], false)], "3.0.0");

        lines.ShouldHaveSingleItem().ShouldContain("src/common/X/.dotskitignore:2 Locked.cs");
    }

    /// <summary>
    /// A file may change between the read of the rules and the write: a rule whose line the file no longer has is
    /// reported, not dropped, so a spent pin does not leave the review unspoken.
    /// </summary>
    [Test]
    public void ASpentRule_WhoseLineTheFileNoLongerHas_IsReportedRatherThanDropped()
    {
        var file = DotskitIgnore.Parse("", "Locked.cs@2.9\n");
        var rule = file.Rules[0] with { Line = 9 };

        var (text, lines) = PinReview.Of(file, [(rule, false)], "3.0.0");

        text.ShouldBe("Locked.cs@2.9\n", customMessage: "the tool writes no line it has no rule for");
        lines.ShouldHaveSingleItem().ShouldContain("no longer in the file");
    }

    [Test]
    public void NoFile_MeansNoRules()
    {
        DotskitIgnore.Parse("", null).IsEmpty.ShouldBeTrue();
        DotskitIgnore.Parse("", "").IsEmpty.ShouldBeTrue();
        DotskitIgnore.Parse("", " # a comment only\n\n").IsEmpty.ShouldBeTrue();
    }
}

internal static class PinTestText
{
    /// <summary>The pin in the words the file writes it.</summary>
    public static string PinText(this Pin pin) => pin.EveryMajor ? "major" : pin.Minor is { } minor ? $"{pin.Major}.{minor}" : pin.Major.ToString();
}

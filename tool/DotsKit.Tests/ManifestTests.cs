using DotsKit.Solutions;

namespace DotsKit.Tests;

[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class ManifestTests
{
    private static readonly Manifest Orders = new()
    {
        Template = "2.8.0",
        Solution = ["--NamespaceRoot", "Acme", "--ProductName", "Shop"],
        Services = [["--ServiceNameOrCustom", "Orders", "--HttpPort", "5000"]],
    };

    [Test]
    public void Should_AddAService_And_WidenTheSolutionByIt()
    {
        var next = Orders.WithService(TemplateArgs.Parse(["-S", "Billing", "--Storage", "true"]));

        next.ServiceArgs.Select(s => s.Service).ShouldBe(["Orders", "Billing"]);
        next.SolutionArgs.IsOn("Storage").ShouldBeTrue();
    }

    [Test]
    public void Should_JoinTheFlags_When_TheServiceIsThereAlready()
    {
        var next = Orders.WithService(TemplateArgs.Parse(["-S", "orders", "--MongoDB", "true"]));

        var orders = next.ServiceArgs.ShouldHaveSingleItem();
        orders.IsOn("MongoDB").ShouldBeTrue();
        orders.Port.ShouldBe(5000);
    }

    [Test]
    public void Should_ChangeNothing_When_TheSameCommandRunsAgain()
    {
        var once = Orders.WithService(TemplateArgs.Parse(["-S", "Billing", "--Storage", "true", "--HttpPort", "5001"]));

        var twice = once.WithService(TemplateArgs.Parse(["-S", "Billing", "--Storage", "true", "--HttpPort", "5001"]));

        twice.Serialize().ShouldBe(once.Serialize());
    }

    [Test]
    public void Should_GiveTheSameSolution_Whatever_TheOrderServicesCameIn()
    {
        var billing = TemplateArgs.Parse(["-S", "Billing", "--Storage", "true", "--HttpPort", "5001"]);
        var stock = TemplateArgs.Parse(["-S", "Stock", "-H", "true", "--HttpPort", "5002"]);

        var one = Orders.WithService(billing).WithService(stock).Solution;
        var other = Orders.WithService(stock).WithService(billing).Solution;

        one.ShouldBe(other);
        TemplateArgs.Parse(one).IsOn("Storage").ShouldBeTrue();
        TemplateArgs.Parse(one).IsOn("Hangfire").ShouldBeTrue();
    }

    [Test]
    public void Should_GiveTheSolutionJobs_When_AServiceNamesNoHangfire() =>
        Orders.WithService(TemplateArgs.Parse(["-S", "Billing"])).SolutionArgs.IsOn("Hangfire").ShouldBeTrue();

    [Test]
    public void Should_NameASolutionValue_When_AServiceCommandGivesAnotherOne() =>
        Orders.OtherSolutionValues(TemplateArgs.Parse(["-S", "Billing", "--GitHubCiCd", "true", "--Database", "postgres"]))
            .ShouldBe(["GitHubCiCd"]);

    [TestCase("null")]
    [TestCase("""{ "template": "2.8.0", "solution": [], "services": [] }""")]
    [TestCase("""{ "template": "2.8.0", "solution": [], "services": [["--Storage", "true"]] }""")]
    public void Should_Refuse_When_TheManifestCannotRebuildTheSolution(string text) =>
        Should.Throw<InvalidOperationException>(() => Manifest.Parse(text)).Message.ShouldContain("put it back from git");

    [Test]
    public void Should_ReadBack_What_ItWrote()
    {
        var root = Io.Folders.NewTemp("manifest");
        Directory.CreateDirectory(Path.Combine(root, ".dotskit"));
        File.WriteAllText(Path.Combine(root, Layout.ManifestPath), Orders.Serialize());

        Manifest.Load(root)!.Serialize().ShouldBe(Orders.Serialize());
    }

    [Test]
    public void Should_GiveTheFirstFreePort_When_TheCommandNamesNone() =>
        Ports.Assign(TemplateArgs.Parse(["-S", "Billing"]), Orders).Port.ShouldBe(5001);

    [Test]
    public void Should_KeepAServicesPort_When_ItsCommandRunsAgainWithoutOne() =>
        Ports.Assign(TemplateArgs.Parse(["-S", "Orders", "--MongoDB", "true"]), Orders).Has("HttpPort").ShouldBeFalse();

    [Test]
    public void Should_Refuse_When_ThePortIsNotANumber() =>
        Should.Throw<ArgumentException>(() => Ports.Assign(TemplateArgs.Parse(["-S", "Billing", "--HttpPort", "abc"]), Orders));

    [Test]
    public void Should_Refuse_When_AnotherServiceHasThePort() =>
        Should.Throw<ArgumentException>(() => Ports.Assign(TemplateArgs.Parse(["-S", "Billing", "--HttpPort", "5000"]), Orders));

    [Test]
    public void Should_KeepThePort_When_TheCommandNamesOne() =>
        Ports.Assign(TemplateArgs.Parse(["-S", "Billing", "--HttpPort", "7000"]), Orders).Port.ShouldBe(7000);

    [Test]
    public void Should_AddWhatTheTemplateAdded_And_RemoveOnlyWhatItRemoved()
    {
        HashSet<string> solution = ["a", "b", "mine"], @base = ["a", "b"], next = ["a", "c"];

        var change = MembershipChange.Of(solution, @base, next);

        change.Add.ShouldBe(["c"]);
        change.Remove.ShouldBe(["b"]);
    }

    [Test]
    public void Should_NotAddBack_AProjectTheTeamRemoved_TheTemplateStillHas()
    {
        HashSet<string> solution = ["a"], @base = ["a", "testing"], next = ["a", "testing", "c"], unchanged = ["a", "testing"];

        MembershipChange.Of(solution, @base, next).Add.ShouldBe(["c"]);
        MembershipChange.Of(solution, @base, unchanged).IsEmpty.ShouldBeTrue();
    }
}

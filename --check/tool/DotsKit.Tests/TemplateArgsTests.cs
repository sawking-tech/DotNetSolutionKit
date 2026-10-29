namespace DotsKit.Tests;

[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class TemplateArgsTests
{
    private static TemplateArgs Parse(params string[] args) => TemplateArgs.Parse(args);

    [TestCase("--Storage")]
    [TestCase("--storage")]
    [TestCase("--STORAGE")]
    public void Should_ReadAFlag_When_ItIsGivenInAnyCase(string name) =>
        Parse("-S", "Orders", name, "true").IsOn("Storage").ShouldBeTrue();

    [Test]
    public void Should_ReadAKebabCaseName_As_TheTemplatesParameter() =>
        Parse("-S", "Gate", "--api-gateway", "true").IsOn("ApiGateway").ShouldBeTrue();

    [Test]
    public void Should_RefuseAServiceName_When_ItHasNoValue() =>
        Should.Throw<ArgumentException>(() => Parse("-S", "--Storage", "true")).Message.ShouldContain("-S");

    [Test]
    public void Should_RefuseAnArgument_When_TheTemplateHasNoSuchParameter() =>
        Should.Throw<ArgumentException>(() => Parse("-S", "Orders", "--Nonsense", "true"));

    [Test]
    public void Should_TakeAFlagWithNoValue_As_On() =>
        Parse("-S", "Orders", "--Solution").AsksForSolution.ShouldBeTrue();

    [Test]
    public void Should_TakeMinimalFalse_As_AskingForTheSolution() =>
        Parse("-S", "Orders", "-M", "false").AsksForSolution.ShouldBeTrue();

    [Test]
    public void Should_SplitACommand_Into_TheSolutionsAndTheServicesParts()
    {
        var command = Parse("-N", "Acme", "-P", "Shop", "-S", "Orders", "--Solution", "--Database", "mssql", "-H", "true", "--HttpPort", "5001");

        var solution = command.SolutionPart();
        var service = command.ServicePart();

        solution.Has("ServiceNameOrCustom").ShouldBeFalse();
        solution.Has("HttpPort").ShouldBeFalse();
        solution.Has("Solution").ShouldBeFalse();
        solution["Database"].ShouldBe("mssql");
        service.Service.ShouldBe("Orders");
        service.Has("NamespaceRoot").ShouldBeFalse();
        service.Has("Database").ShouldBeFalse();
        service.Has("Solution").ShouldBeFalse();
        service.IsOn("Hangfire").ShouldBeTrue();
    }

    [Test]
    public void Should_WidenTheSolution_By_WhatAServiceBringsOutsideItsFolder()
    {
        var solution = Parse("-N", "Acme", "-P", "Shop", "--Messaging", "direct", "--Notify", "email");
        var service = Parse("-S", "Billing", "--Storage", "true", "-H", "true", "--Messaging", "outbox", "--Notify", "sms");

        var widened = solution.WidenBy(service);

        widened.IsOn("Storage").ShouldBeTrue();
        widened.IsOn("Hangfire").ShouldBeTrue();
        widened["Messaging"].ShouldBe("outbox");
        widened["Notify"].ShouldBe("email,sms");
    }

    [Test]
    public void Should_KeepTheSolutionsStrongerBus_When_AServiceHasAWeakerOne() =>
        Parse("--Messaging", "outbox").WidenBy(Parse("-S", "Billing", "--Messaging", "none"))["Messaging"].ShouldBe("outbox");

    [TestCase("2.8.0", "Solution")]
    [TestCase("source", "Solution")]
    [TestCase("2.7.0", "Minimal")]
    public void Should_AskForTheSolution_In_TheWordsOfTheVersion(string version, string mode)
    {
        var command = TemplateArgs.ForSolution(Parse("-N", "Acme"), Parse("-S", "Orders"), version);

        command.Has(mode).ShouldBeTrue();
        command.AsksForSolution.ShouldBeTrue();
        command.Service.ShouldBe("Orders");
    }

    [Test]
    public void Should_GenerateAServiceWithItsOwnFlags_NotTheSolutionsWidenedOnes()
    {
        var solution = Parse("-N", "Acme", "--Database", "mssql", "--Storage", "true", "--Messaging", "outbox");

        var command = TemplateArgs.ForService(solution, Parse("-S", "Billing", "-H", "true"));

        command["Database"].ShouldBe("mssql");
        command.Has("Storage").ShouldBeFalse();
        command.Has("Messaging").ShouldBeFalse();
        command.AsksForSolution.ShouldBeFalse();
        command.IsOn("Hangfire").ShouldBeTrue();
    }
}

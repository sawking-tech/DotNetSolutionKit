using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace NamespaceRoot.ProductName.Capabilities.Notifications.Tests;

/// <summary>What the Email section requires at startup, and which transport the provider it names gets.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class EmailRegistrationTests
{
    public static ServiceProvider Build(Dictionary<string, string?> settings, string environment = "Production")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new HostEnvironment(environment));
        services.AddLogging();
        services.AddNotifications(configuration);
        return services.BuildServiceProvider();
    }

    private static OptionsValidationException Invalid(Dictionary<string, string?> settings)
    {
        using var provider = Build(settings);
        return Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<NotificationEmailSettings>>().Value);
    }

    [Test(Description = "A provider has to be named")]
    public void Should_RequireAProvider()
    {
        Invalid([]).Message.ShouldContain("Email Provider must be set to GraphApi or Smtp.");
    }

    [Test(Description = "With SMTP, its host and its sender are required")]
    public void Should_RequireTheSmtpSection_When_TheProviderIsSmtp()
    {
        var failure = Invalid(new() { ["Email:Provider"] = "Smtp", ["Email:Smtp:Port"] = "587" });

        failure.Message.ShouldContain("Email:Smtp:");
        failure.Message.ShouldContain("Host");
        failure.Message.ShouldContain("From");
    }

    [Test(Description = "With Graph API, its section is required")]
    public void Should_RequireTheGraphSection_When_TheProviderIsGraph()
    {
        Invalid(new() { ["Email:Provider"] = "GraphApi" }).Message.ShouldContain("Email:GraphApi is required");
    }

    [Test(Description = "The sandbox address has to be an email address")]
    public void Should_RequireASandboxAddress()
    {
        var failure = Invalid(new()
        {
            ["Email:Provider"] = "Smtp",
            ["Email:Smtp:Host"] = "localhost",
            ["Email:Smtp:From"] = "noreply@example.com",
            ["Email:Sandbox:ReceiverAddress"] = "not-an-address",
        });

        failure.Message.ShouldContain("Email:Sandbox:");
        failure.Message.ShouldContain("ReceiverAddress");
    }

    [Test(Description = "With SMTP, a user name without its password, or the other way round, is refused")]
    public void Should_RequireTheSmtpSignInWhole()
    {
        Invalid(new()
        {
            ["Email:Provider"] = "Smtp",
            ["Email:Smtp:Host"] = "localhost",
            ["Email:Smtp:From"] = "noreply@example.com",
            ["Email:Smtp:Username"] = "mailer",
        }).Message.ShouldContain("Username and Password are set together or not at all.");
    }

    [Test(Description = "The transport of the named provider is the one registered")]
    public void Should_RegisterTheTransportOfTheProvider_When_GraphApi()
    {
        using var provider = Build(new()
        {
            ["Email:Provider"] = "GraphApi",
            ["Email:GraphApi:TenantId"] = "t",
            ["Email:GraphApi:ClientId"] = "c",
            ["Email:GraphApi:ClientSecret"] = "test-do-not-use",
            ["Email:GraphApi:SenderEmail"] = "mailbox@example.com",
        });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<INotificationEmailSender>().ShouldNotBeNull();
        scope.ServiceProvider.GetRequiredService<IEmailTransport>().ShouldBeOfType<GraphApiEmailTransport>();
        provider.GetRequiredService<IOptions<NotificationEmailSettings>>().Value.Provider.ShouldBe(EmailProviderType.GraphApi);
    }

    [Test(Description = "The transport of the named provider is the one registered")]
    public void Should_RegisterTheTransportOfTheProvider_When_Smtp()
    {
        using var provider = Build(new()
        {
            ["Email:Provider"] = "Smtp",
            ["Email:Smtp:Host"] = "localhost",
            ["Email:Smtp:From"] = "noreply@example.com",
        });
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEmailTransport>().ShouldBeOfType<SmtpEmailTransport>();
        provider.GetRequiredService<IOptions<NotificationEmailSettings>>().Value.Provider.ShouldBe(EmailProviderType.Smtp);
    }

    private sealed class HostEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

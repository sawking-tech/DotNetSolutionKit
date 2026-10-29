using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace NamespaceRoot.ProductName.Capabilities.Notifications.Tests.Integration;

/// <summary>
/// The SMTP transport against a real server: MailHog, named by <c>TEST_SMTP</c> (<c>host:port</c>), whose
/// HTTP API, named by <c>TEST_SMTP_API</c>, shows what arrived.
/// </summary>
[TestFixture]
[Category(TestCategories.Integration)]
[NonParallelizable]
internal class SmtpTests
{
    private string _host = null!;
    private int _port;
    private HttpClient _api = null!;

    [SetUp]
    public async Task Connect()
    {
        var smtp = Environment.GetEnvironmentVariable("TEST_SMTP");
        var api = Environment.GetEnvironmentVariable("TEST_SMTP_API");
        if (string.IsNullOrWhiteSpace(smtp) || string.IsNullOrWhiteSpace(api))
            Assert.Ignore("TEST_SMTP and TEST_SMTP_API are not set: no SMTP server to run integration tests against.");

        var parts = smtp!.Split(':');
        (_host, _port) = (parts[0], int.Parse(parts[1]));
        _api = new HttpClient { BaseAddress = new Uri(api!) };
        (await _api.DeleteAsync("api/v1/messages")).EnsureSuccessStatusCode();
    }

    [TearDown]
    public void Disconnect() => _api?.Dispose();

    [Test(Description = "In Production, with the sandbox off, a message arrives at its recipient as it was sent")]
    public async Task Should_Deliver_InProduction()
    {
        await SendAsync("Production", sandboxEnabled: false);

        var arrived = await ArrivedAsync();
        Header(arrived, "Subject").ShouldBe("Your code");
        Header(arrived, "To").ShouldBe("customer@example.test");
        Body(arrived).ShouldNotContain("[SANDBOX INTERCEPTED]");
    }

    [Test(Description = "Outside Production the message arrives at the sandbox address, not at its recipient")]
    public async Task Should_DivertToTheSandbox_OutsideProduction()
    {
        await SendAsync("Staging", sandboxEnabled: false);

        var arrived = await ArrivedAsync();
        Header(arrived, "To").ShouldBe("sandbox@example.test");
        Body(arrived).ShouldContain("[SANDBOX INTERCEPTED]");
        Body(arrived).ShouldContain("customer@example.test");
    }

    private async Task SendAsync(string environment, bool sandboxEnabled)
    {
        using var provider = EmailRegistrationTests.Build(new()
        {
            ["Email:Provider"] = "Smtp",
            ["Email:Smtp:Host"] = _host,
            ["Email:Smtp:Port"] = _port.ToString(),
            ["Email:Smtp:From"] = "noreply@example.test",
            ["Email:Sandbox:Enabled"] = sandboxEnabled.ToString(),
            ["Email:Sandbox:ReceiverAddress"] = "sandbox@example.test",
        }, environment);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<INotificationEmailSender>()
            .SendEmailAsync("customer@example.test", "Your code", "Your code is 123456.", CancellationToken.None);
    }

    /// <summary>The one message MailHog holds, waiting a moment for it to land.</summary>
    private async Task<JsonElement> ArrivedAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var list = await _api.GetFromJsonAsync<JsonElement>("api/v2/messages");
            var messages = list.GetProperty("items");
            if (messages.GetArrayLength() > 0)
                return messages[0];
            await Task.Delay(100);
        }

        throw new InvalidOperationException("No message arrived at the SMTP server.");
    }

    private static string Body(JsonElement message) =>
        message.GetProperty("Content").GetProperty("Body").GetString() ?? string.Empty;

    private static string? Header(JsonElement message, string name) =>
        message.GetProperty("Content").GetProperty("Headers").GetProperty(name)[0].GetString();
}

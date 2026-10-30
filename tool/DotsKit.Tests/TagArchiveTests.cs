using DotsKit.Generation;

namespace DotsKit.Tests;

/// <summary>A release before nuget.org comes from its tag on GitHub; when the network fails, the command says so.</summary>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal sealed class TagArchiveTests
{
    /// <summary>Answers every request the way HttpClient reports its own timeout.</summary>
    private sealed class TimingOut : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
    }

    [Test]
    public async Task ATimeout_IsAFetchThatFailed_NotACancel()
    {
        using var http = new HttpClient(new TimingOut());

        var failed = await Should.ThrowAsync<InvalidOperationException>(() => new TagArchive(http).FolderOfAsync("1.0.0", CancellationToken.None));

        failed.Message.ShouldContain("could not be fetched");
        failed.Message.ShouldContain("check the network");
    }

    /// <summary>Records every request; none should be made.</summary>
    private sealed class Recording : HttpMessageHandler
    {
        public List<Uri?> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    [TestCase(@"1.0.0-\..\..\..\..\evil\repo\archive\refs\tags\v1")]
    [TestCase("1.0.0-/../../../evil/repo/archive/refs/tags/v1")]
    [TestCase("1.0.0-x?y=1")]
    [TestCase("source")]
    public async Task AStringThatIsNoRelease_IsRefused_BeforeAnyRequest(string version)
    {
        var handler = new Recording();
        using var http = new HttpClient(handler);

        var refused = await Should.ThrowAsync<InvalidOperationException>(() => new TagArchive(http).FolderOfAsync(version, CancellationToken.None));

        refused.Message.ShouldContain("is not a release of the template");
        handler.Requests.ShouldBeEmpty();
    }

    [Test]
    public async Task ACancelTheUserAskedFor_StaysACancel()
    {
        using var http = new HttpClient(new TimingOut());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => new TagArchive(http).FolderOfAsync("1.0.0", cancelled.Token));
    }

    [Test]
    public void TheReleases_AreTheTagsOfAVersion_AndAPreReleaseIsNone()
    {
        const string listed = "a1\trefs/tags/v1.0.0\r\nb2\trefs/tags/v2.0.0-preview.1\nc3\trefs/tags/v2.6.2\nd4\trefs/tags/backup\n";

        GitTemplateReleases.Parse(listed).ShouldBe(["1.0.0", "2.6.2"]);
    }

    [Test]
    public void ASolutionThatDoesNotSayItsVersion_MayBeOfTheLastPatchOfEachMinorBefore27() =>
        TemplateVersions.Undeclared(["2.6.1", "1.0.0", "2.1.0", "2.6.2", "2.1.3", "2.7.0", "2.8.0"]).ShouldBe(["1.0.0", "2.1.3", "2.6.2"]);
}

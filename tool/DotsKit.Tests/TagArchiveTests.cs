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

    [Test]
    public async Task ACancelTheUserAskedFor_StaysACancel()
    {
        using var http = new HttpClient(new TimingOut());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => new TagArchive(http).FolderOfAsync("1.0.0", cancelled.Token));
    }
}

using System.Formats.Tar;
using System.IO.Compression;
using DotsKit.Io;

namespace DotsKit.Generation;

/// <summary>
/// The template of a release that is not on nuget.org, before 2.6.1: the archive of its tag on GitHub, unpacked,
/// and the template's folder in it, which <c>dotnet new install</c> takes as it takes a package.
/// </summary>
internal interface ITagArchive
{
    Task<string> FolderOfAsync(string version, CancellationToken ct);
}

internal sealed class TagArchive(HttpClient http) : ITagArchive
{
    private const string ArchiveUrl = Repository.Url + "/archive/refs/tags/v{0}.tar.gz";

    /// <summary>Where the template lives in the repository, in every tag from 1.0.0.</summary>
    private const string TemplateFolder = "template";

    public async Task<string> FolderOfAsync(string version, CancellationToken ct)
    {
        var url = string.Format(System.Globalization.CultureInfo.InvariantCulture, ArchiveUrl, version);
        try
        {
            return await FetchAsync(version, url, ct);
        }
        // HttpClient reports its own timeout as a cancel; a cancel the user asked for is ct's and goes on as one.
        catch (Exception e) when (e is HttpRequestException or InvalidDataException || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new InvalidOperationException($"The template {version} is not on nuget.org, and its tag could not be fetched from {url} ({e.Message}): check the network and run again.");
        }
    }

    private async Task<string> FetchAsync(string version, string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"The template {version} is not on nuget.org, and its tag cannot be fetched ({url}: {(int)response.StatusCode}): name a released version, or check the network.");
        }

        var target = Folders.NewTemp("tag-" + version);
        await using (var gzip = new GZipStream(await response.Content.ReadAsStreamAsync(ct), CompressionMode.Decompress))
        {
            await TarFile.ExtractToDirectoryAsync(gzip, target, overwriteFiles: true, ct);
        }
        // GitHub puts the tree in a folder named after the repository and the tag.
        return Path.Combine(Directory.GetDirectories(target).Single(), TemplateFolder);
    }
}

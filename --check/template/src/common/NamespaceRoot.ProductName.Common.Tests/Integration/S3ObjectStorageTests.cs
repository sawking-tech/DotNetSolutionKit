using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NamespaceRoot.ProductName.Common.Exceptions;
using NamespaceRoot.ProductName.Common.Infrastructure.Storage;

namespace NamespaceRoot.ProductName.Common.Tests.Integration;

/// <summary>
/// Object storage against a real S3-compatible server, from <c>TEST_S3</c>:
/// <c>ServiceUrl=http://localhost:9000;Bucket=tests;AccessKey=...;SecretKey=...</c>, the bucket existing.
/// Skipped when it is not set.
/// </summary>
[TestFixture]
[Category(TestCategories.Integration)]
internal class S3ObjectStorageTests
{
    private IS3ObjectStorage _storage = null!;
    private string _prefix = null!;

    [SetUp]
    public void Connect()
    {
        var value = Environment.GetEnvironmentVariable("TEST_S3");
        if (string.IsNullOrWhiteSpace(value))
            Assert.Ignore("TEST_S3 is not set: no S3-compatible server to run against.");

        var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => (string?)p[1]);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["S3:ServiceUrl"] = parts["ServiceUrl"],
            ["S3:BucketName"] = parts["Bucket"],
            ["S3:AccessKey"] = parts["AccessKey"],
            ["S3:SecretKey"] = parts["SecretKey"],
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddS3ObjectStorage(configuration);
        _storage = services.BuildServiceProvider().GetRequiredService<IS3ObjectStorage>();
        _prefix = $"tests/{Guid.NewGuid():N}/";
    }

    [Test(Description = "What is put is read back, listed, copied and deleted")]
    public async Task Should_StoreReadListCopyAndDelete()
    {
        await _storage.PutAsync(_prefix + "a.txt", Encoding.UTF8.GetBytes("hello"), "text/plain");

        (await _storage.GetTextAsync(_prefix + "a.txt")).ShouldBe("hello");
        (await _storage.ExistsAsync(_prefix + "a.txt")).ShouldBeTrue();

        await _storage.CopyAsync(_prefix + "a.txt", _prefix + "b.txt");
        (await _storage.ListKeysAsync(_prefix)).OrderBy(k => k).ShouldBe([_prefix + "a.txt", _prefix + "b.txt"]);

        await _storage.DeleteAsync(_prefix + "a.txt");
        await _storage.DeleteAsync(_prefix + "b.txt");
        (await _storage.ExistsAsync(_prefix + "a.txt")).ShouldBeFalse();
    }

    [Test(Description = "An empty prefix lists nothing rather than failing")]
    public async Task Should_ListNothing_When_ThePrefixIsEmpty() =>
        (await _storage.ListKeysAsync(_prefix)).ShouldBeEmpty();

    [Test(Description = "A missing object is not found; deleting it is a no-op")]
    public async Task Should_ReportAMissingObject()
    {
        await Should.ThrowAsync<NotFoundException>(() => _storage.GetBytesAsync(_prefix + "missing"));
        await _storage.DeleteAsync(_prefix + "missing");
    }
}

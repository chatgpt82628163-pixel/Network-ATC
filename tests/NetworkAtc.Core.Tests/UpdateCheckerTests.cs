using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NetworkAtc.Core.Updates;

namespace NetworkAtc.Core.Tests;

/// <summary>The program finds a newer release on GitHub, downloads its installer and checks it before running it.</summary>
public class UpdateCheckerTests
{
    /// <summary>Serves the release JSON for the API and the given bytes for any download.</summary>
    private sealed class ReleaseServer(string json, byte[]? download = null) : HttpMessageHandler
    {
        public HttpRequestMessage? LastApiRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/releases/latest", StringComparison.Ordinal))
            {
                LastApiRequest = request;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
            }
            return Task.FromResult(download == null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(download) });
        }
    }

    private static string Release(string tag, string assets = "") => $$"""
        { "tag_name": "{{tag}}", "html_url": "https://github.com/Anntixs/Network-ATC/releases/tag/{{tag}}", "assets": [ {{assets}} ] }
        """;

    private static string Asset(string name, long size, string? sha = null) =>
        $$"""{ "name": "{{name}}", "browser_download_url": "https://example.test/{{name}}", "size": {{size}}{{(sha == null ? "" : $", \"digest\": \"sha256:{sha}\"")}} }""";

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] Bytes(int n)
    {
        var b = new byte[n];
        new Random(n).NextBytes(b);
        return b;
    }

    private static string TempDir() => Path.Combine(Path.GetTempPath(), "natc-update-test-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ANewerRelease_IsFoundWithItsInstaller()
    {
        var server = new ReleaseServer(Release("0.2.0", Asset("Network-ATC-0.2.0-win-x64.zip", 1) + "," + Asset("Network-ATC-Setup-0.2.0.exe", 12345, new string('a', 64))));
        var info = await new UpdateChecker(new HttpClient(server)).CheckAsync(new Version(0, 1, 1, 0));

        Assert.NotNull(info);
        Assert.Equal((new Version(0, 2, 0), "0.2.0"), (info!.Version, info.Tag));
        Assert.Equal("https://example.test/Network-ATC-Setup-0.2.0.exe", info.SetupUrl!.ToString());
        Assert.Equal((12345L, new string('a', 64), false), (info.SetupSize, info.SetupSha256, info.SetupIsZip));
        Assert.Equal("https://api.github.com/repos/Anntixs/Network-ATC/releases/latest", server.LastApiRequest!.RequestUri!.ToString());
        Assert.Contains("Network-ATC", server.LastApiRequest.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task SameOrOlder_OrAlreadyInstalledByUs_IsNothing()
    {
        var checker = new UpdateChecker(new HttpClient(new ReleaseServer(Release("v0.3.0", Asset("Network-ATC-Setup-0.3.0.exe", 5000)))));
        Assert.Null(await checker.CheckAsync(new Version(0, 3, 0, 0)));   // the program's own version has four parts
        Assert.Null(await checker.CheckAsync(new Version(1, 0, 0)));
        // A release whose installer says it is an older version: offered once, not again after installing it.
        Assert.NotNull(await checker.CheckAsync(new Version(0, 1, 0)));
        Assert.Null(await checker.CheckAsync(new Version(0, 1, 0), installedTag: "v0.3.0"));
        Assert.NotNull(await checker.CheckAsync(new Version(0, 1, 0), installedTag: "0.2.0"));
    }

    [Fact]
    public async Task NoReleasesGarbageOrPrerelease_IsNothing()
    {
        Assert.Null(await new UpdateChecker(new HttpClient(new ReleaseServer("not json"))).CheckAsync(new Version(0, 1)));
        Assert.Null(await new UpdateChecker(new HttpClient(new ReleaseServer("""{ "tag_name": "msfs-sdk" }"""))).CheckAsync(new Version(0, 1)));
        Assert.Null(await new UpdateChecker(new HttpClient(new ReleaseServer("""{ "tag_name": "0.9.0", "prerelease": true }"""))).CheckAsync(new Version(0, 1)));
        // A release without an installer is still reported, with its page, so the user can get it by hand.
        var bare = await new UpdateChecker(new HttpClient(new ReleaseServer(Release("0.9.0")))).CheckAsync(new Version(0, 1));
        Assert.Null(bare!.SetupUrl);
        Assert.Equal("https://github.com/Anntixs/Network-ATC/releases/tag/0.9.0", bare.Page.ToString());
    }

    [Fact]
    public async Task TheInstallerIsDownloadedAndChecked()
    {
        var setup = Bytes(5000);
        var checker = new UpdateChecker(new HttpClient(new ReleaseServer(Release("0.2.0", Asset("Network-ATC-Setup-0.2.0.exe", setup.Length, Sha(setup))), setup)));
        var info = (await checker.CheckAsync(new Version(0, 1)))!;
        string dir = TempDir();
        try
        {
            var reported = new List<double>();
            string path = await checker.DownloadSetupAsync(info, dir, new Progress<double>(reported.Add));
            Assert.Equal(Path.Combine(dir, "Network-ATC-Setup-0.2.0.exe"), path);
            Assert.Equal(setup, await File.ReadAllBytesAsync(path));
            Assert.False(File.Exists(path + ".part"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task AShortOrDamagedDownload_IsNeverKept()
    {
        var setup = Bytes(5000);
        var checker = new UpdateChecker(new HttpClient(new ReleaseServer(Release("0.2.0", Asset("Network-ATC-Setup-0.2.0.exe", setup.Length, new string('0', 64))), setup)));
        var info = (await checker.CheckAsync(new Version(0, 1)))!;
        string dir = TempDir();
        try
        {
            var damaged = await Assert.ThrowsAsync<IOException>(() => checker.DownloadSetupAsync(info, dir));
            Assert.Contains("checksum", damaged.Message);
            await Assert.ThrowsAsync<IOException>(() => checker.DownloadSetupAsync(info with { SetupSize = 6000, SetupSha256 = null }, dir));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task AZipWithTheInstallerInside_IsUnpacked()
    {
        var setup = Bytes(4000);
        byte[] zip;
        using (var ms = new MemoryStream())
        {
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            await using (var entry = archive.CreateEntry("Network-ATC-Setup-0.1.0.exe").Open())
                await entry.WriteAsync(setup);
            zip = ms.ToArray();
        }
        var checker = new UpdateChecker(new HttpClient(new ReleaseServer(Release("0.1.2", Asset("Network-ATC-Setup-0.1.0.zip", zip.Length, Sha(zip))), zip)));
        var info = (await checker.CheckAsync(new Version(0, 1, 0)))!;
        Assert.True(info.SetupIsZip);
        string dir = TempDir();
        try
        {
            string path = await checker.DownloadSetupAsync(info, dir);
            Assert.Equal("Network-ATC-Setup-0.1.0.exe", Path.GetFileName(path));
            Assert.Equal(setup, await File.ReadAllBytesAsync(path));
            Assert.False(File.Exists(Path.Combine(dir, "Network-ATC-Setup-0.1.0.zip")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData("v0.2.0", "0.2.0")]
    [InlineData("0.2.1", "0.2.1")]
    [InlineData("v1.0.0-beta.1", "1.0.0")]
    [InlineData("v2", "2.0.0")]
    [InlineData("v1.2.3.4", "1.2.3")]
    public void ParsesTags(string tag, string expected)
    {
        Assert.True(UpdateChecker.TryParseTag(tag, out var version));
        Assert.Equal(Version.Parse(expected), version);
    }

    [Fact]
    public void RejectsNonVersionTags() => Assert.False(UpdateChecker.TryParseTag("msfs-sdk", out _));
}

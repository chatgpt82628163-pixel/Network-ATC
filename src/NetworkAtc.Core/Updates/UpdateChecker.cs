using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace NetworkAtc.Core.Updates;

/// <param name="Tag">The release tag as written on GitHub ("0.2.0", "v0.2.0").</param>
/// <param name="SetupUrl">Download of the installer, or of a zip with the installer inside; null when the release has neither.</param>
/// <param name="SetupSha256">The checksum GitHub keeps for the download (lower-case hex), when it has one.</param>
public sealed record ReleaseInfo(Version Version, string Tag, Uri Page, Uri? SetupUrl, long SetupSize, string? SetupSha256 = null)
{
    public bool SetupIsZip => SetupUrl?.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// Looks up the newest release on GitHub and fetches its installer, which the program then runs silently to update
/// itself. Any failure of the check (offline, no releases yet, rate limit) means "nothing new".
/// </summary>
public sealed class UpdateChecker(HttpClient http, string repository = UpdateChecker.DefaultRepository, string setupPrefix = UpdateChecker.DefaultSetupPrefix)
{
    public const string DefaultRepository = "Anntixs/Network-ATC";
    public const string DefaultSetupPrefix = "Network-ATC-Setup-";

    /// <summary>
    /// How the downloaded installer is run: no questions, closes the program if it still runs, and starts the new
    /// version when done (the /LAUNCH=1 switch of installer/Network-ATC.iss).
    /// </summary>
    public const string SilentArguments = "/SILENT /SP- /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /LAUNCH=1";

    public Uri LatestReleaseApi => new($"https://api.github.com/repos/{repository}/releases/latest");

    /// <summary>
    /// The newest release if it is newer than <paramref name="current"/>, otherwise null. A release whose tag is
    /// <paramref name="installedTag"/> (the one this program last installed by itself) is not offered again, so a
    /// release whose installer carries an older version number cannot be offered over and over.
    /// </summary>
    public async Task<ReleaseInfo?> CheckAsync(Version current, string? installedTag = null, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
            request.Headers.UserAgent.ParseAdd(UserAgent);
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK) return null;
            var release = await response.Content.ReadFromJsonAsync<ReleaseDto>(ct).ConfigureAwait(false);
            if (release?.Tag == null || release.Draft || release.Prerelease || !TryParseTag(release.Tag, out var version)) return null;
            if (version <= Normalize(current)) return null;
            if (installedTag != null && string.Equals(installedTag.Trim(), release.Tag.Trim(), StringComparison.OrdinalIgnoreCase)) return null;
            var page = Uri.TryCreate(release.Url, UriKind.Absolute, out var uri) ? uri : new Uri($"https://github.com/{repository}/releases/latest");
            var setup = Asset(release, ".exe") ?? Asset(release, ".zip");
            return new ReleaseInfo(version, release.Tag.Trim(), page, setup == null ? null : new Uri(setup.Url!), setup?.Size ?? 0, Sha256Of(setup));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private string UserAgent => setupPrefix.TrimEnd('-').Replace("-Setup", "", StringComparison.OrdinalIgnoreCase);

    // The installer itself ("Network-ATC-Setup-0.2.0.exe"), or a zip with it inside, as a build artifact uploaded by hand.
    private AssetDto? Asset(ReleaseDto release, string extension) => release.Assets?.FirstOrDefault(a =>
        a.Name != null && a.Name.StartsWith(setupPrefix, StringComparison.OrdinalIgnoreCase) &&
        a.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(a.Url, UriKind.Absolute, out _));

    private static string? Sha256Of(AssetDto? asset) =>
        asset?.Digest is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && d.Length == 7 + 64 ? d[7..].ToLowerInvariant() : null;

    /// <summary>
    /// Downloads the installer into <paramref name="directory"/> and returns the path of the .exe to run (taken out of
    /// the zip when the release has only a zip). Throws on any failure, including a short download or a wrong
    /// checksum, so a damaged file is never run.
    /// </summary>
    public async Task<string> DownloadSetupAsync(ReleaseInfo release, string directory, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (release.SetupUrl == null) throw new InvalidOperationException("The release has no installer");
        Directory.CreateDirectory(directory);
        string fileName = Path.GetFileName(release.SetupUrl.AbsolutePath);
        if (fileName.Length == 0 || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            fileName = setupPrefix + release.Version.ToString(3) + (release.SetupIsZip ? ".zip" : ".exe");
        string target = Path.Combine(directory, fileName);
        string part = target + ".part";

        using var request = new HttpRequestMessage(HttpMethod.Get, release.SetupUrl);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? release.SetupSize;
        long written = 0;
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var file = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                int read;
                while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    sha.AppendData(buffer, 0, read);
                    written += read;
                    if (total > 0) progress?.Report(Math.Min(1, (double)written / total));
                }
            }
            if (written < 1024 || release.SetupSize > 0 && written != release.SetupSize)
                throw new IOException("The update was not downloaded completely");
            if (release.SetupSha256 != null && !string.Equals(Convert.ToHexString(sha.GetHashAndReset()), release.SetupSha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The downloaded update is damaged (checksum mismatch)");
            File.Move(part, target, overwrite: true);
        }
        catch
        {
            File.Delete(part);
            throw;
        }
        return release.SetupIsZip ? ExtractSetup(target, directory) : target;
    }

    /// <summary>The installer out of a zip (a build artifact attached to the release by hand).</summary>
    private string ExtractSetup(string zipPath, string directory)
    {
        try
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.Entries.FirstOrDefault(e => e.Name.StartsWith(setupPrefix, StringComparison.OrdinalIgnoreCase) &&
                                                        e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        ?? throw new IOException("The update archive has no installer inside");
            string exe = Path.Combine(directory, entry.Name);
            entry.ExtractToFile(exe, overwrite: true);
            return exe;
        }
        catch (InvalidDataException e)
        {
            throw new IOException("The update archive is damaged", e);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    /// <summary>"v0.2.0" or "0.2.0" → 0.2.0; anything else is not a release tag.</summary>
    public static bool TryParseTag(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        int dash = text.IndexOf('-');
        if (dash > 0) text = text[..dash];
        if (!text.Contains('.')) text += ".0";
        if (!Version.TryParse(text, out var parsed)) return false;
        version = Normalize(parsed);
        return true;
    }

    /// <summary>Three parts, so 0.2 and 0.2.0.0 (the program's own version) compare equal to the tag 0.2.0.</summary>
    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private sealed class ReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? Tag { get; set; }
        [JsonPropertyName("html_url")] public string? Url { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<AssetDto>? Assets { get; set; }
    }

    private sealed class AssetDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("browser_download_url")] public string? Url { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("digest")] public string? Digest { get; set; }
    }
}

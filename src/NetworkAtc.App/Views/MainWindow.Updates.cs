using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using NetworkAtc.Core.Updates;

namespace NetworkAtc.App.Views;

/// <summary>
/// Updates, like vPilot: at start a newer release on GitHub is offered; on yes its installer is downloaded, checked,
/// and run silently, the program closes, and the installer starts the new version.
/// </summary>
public partial class MainWindow
{
    /// <summary>For the installer download, which takes longer than an API call.</summary>
    private static readonly HttpClient UpdateHttp = new() { Timeout = TimeSpan.FromMinutes(10) };

    private async void CheckForUpdatesAtStart()
    {
        if (!_profile.CheckForUpdates) return;
        try
        {
            // Let the window and the sector come up first.
            await Task.Delay(TimeSpan.FromSeconds(3));
            await CheckForUpdatesAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException
                                       or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Error("Update check failed: " + ex.Message);
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        var current = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
        var checker = new UpdateChecker(UpdateHttp);
        var release = await checker.CheckAsync(current, _profile.UpdateInstalledTag);
        if (release == null) return;
        string version = release.Version.ToString(3);
        if (release.SetupUrl == null)
        {
            Info($"Network-ATC {version} is available: {release.Page}");
            return;
        }
        string connected = _session.IsConnected ? "\n\nYou are connected to the network: the connection will be closed." : "";
        var answer = MessageBox.Show(this,
            $"Network-ATC {version} is available (you have {UpdateChecker.Normalize(current).ToString(3)}).\n\n" +
            $"Install it now? Network-ATC will close, the update installs by itself and the program starts again.{connected}",
            "Network-ATC update", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            Info($"Network-ATC {version} is waiting: {release.Page}. It will be offered again at the next start.");
            return;
        }
        await InstallUpdateAsync(checker, release);
    }

    /// <summary>Downloads the installer and runs it silently; the installer starts the new version itself.</summary>
    private async Task InstallUpdateAsync(UpdateChecker checker, ReleaseInfo release)
    {
        string version = release.Version.ToString(3);
        string directory = Path.Combine(Path.GetTempPath(), "Network-ATC-update");
        Info($"Downloading Network-ATC {version}…");
        string setup;
        try
        {
            int lastQuarter = -1;
            var progress = new Progress<double>(p =>
            {
                int quarter = (int)(p * 4);
                if (quarter <= lastQuarter || quarter >= 4) return;
                lastQuarter = quarter;
                if (quarter > 0) Info($"Downloading the update: {quarter * 25} %");
            });
            setup = await checker.DownloadSetupAsync(release, directory, progress);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            Error("Could not download the update: " + ex.Message);
            Info($"You can get it from {release.Page}");
            return;
        }
        Process.Start(new ProcessStartInfo(setup, UpdateChecker.SilentArguments) { UseShellExecute = true });
        // Remembered, so a release whose installer carries an older version number is not offered again once installed.
        _profile.UpdateInstalledTag = release.Tag;
        // A normal exit: the profile is saved and the network and ATIS connections sign off; the installer waits for it.
        Close();
        Application.Current.Shutdown();
    }
}

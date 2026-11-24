using System.Reflection;
using System.Windows.Threading;
using mk.dev.runner.core;
using mk.dev.runner.desktop.ViewModels;
using Velopack;
using Velopack.Sources;

namespace mk.dev.runner.desktop.Updates;

/// <summary>
/// In-app updates from GitHub Releases (Velopack). It only checks and shows "Update to x.y.z" in the app;
/// nothing downloads or installs until you click it. Stable gets normal releases; Dev also gets pre-releases.
/// Works once Dev Runner is installed with its Setup.exe (running from the dist folder can't update itself).
/// </summary>
public sealed class Updater : Observable
{
    /// <summary>The GitHub repo releases come from.</summary>
    public const string RepoUrl = "https://github.com/mcknight89/mk.dev.runner";

    const string DevChannel = "dev";
    const string StableChannel = "win";   // Velopack's default channel name on Windows
    static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    readonly AppSettings _settings;
    readonly DispatcherTimer _timer;
    UpdateInfo? _found;

    public Updater(AppSettings settings)
    {
        _settings = settings;
        try { IsInstalled = new UpdateManager(new GithubSource(RepoUrl, null, false)).IsInstalled; }
        catch { IsInstalled = false; }
        CurrentVersion = InstalledVersion() ?? AssemblyVersion();
        Status = IsInstalled ? "" : "Updates work once Dev Runner is installed with its Setup.exe.";
        _timer = new DispatcherTimer { Interval = CheckEvery };
        _timer.Tick += async (_, _) => await CheckAsync(manual: false);
    }

    public bool IsInstalled { get; }

    /// <summary>Dev updates: when chosen in Settings, or (nothing chosen yet) when this copy is itself a Dev build.</summary>
    public bool WantsDev => _settings.UpdateChannel is { } chosen ? chosen == DevChannel : CurrentVersion.Contains("-dev");
    public string CurrentVersion { get; }

    string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    string? _available;
    /// <summary>The newer version found, or null.</summary>
    public string? Available { get => _available; private set { if (Set(ref _available, value)) Raise(nameof(HasUpdate)); } }
    public bool HasUpdate => Available != null;

    bool _busy;
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }

    /// <summary>First check a minute after start, then every 6 hours, while "check automatically" is on.</summary>
    public void Start()
    {
        if (!IsInstalled) return;
        var first = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        first.Tick += async (_, _) => { first.Stop(); await CheckAsync(manual: false); };
        first.Start();
        _timer.Start();
    }

    UpdateManager Manager()
    {
        bool dev = WantsDev;
        var source = new GithubSource(RepoUrl, accessToken: null, prerelease: dev);
        // each setting asks for its channel by name, so switching either way works (and Dev → Stable may go down a version)
        var options = new UpdateOptions { ExplicitChannel = dev ? DevChannel : StableChannel, AllowVersionDowngrade = true };
        return new UpdateManager(source, options);
    }

    public async Task CheckAsync(bool manual)
    {
        if (!IsInstalled || Busy) return;
        if (!manual && !_settings.CheckForUpdates) return;
        Busy = true;
        Status = "Checking for updates…";
        try
        {
            _found = await Task.Run(() => Manager().CheckForUpdatesAsync());
            Available = _found?.TargetFullRelease.Version.ToString();
            Status = Available != null ? $"Version {Available} is available." : $"You're up to date ({CurrentVersion}).";
        }
        catch (Exception e)
        {
            _found = null;
            Available = null;
            Status = $"Couldn't check for updates: {e.Message}";
        }
        finally { Busy = false; }
    }

    /// <summary>Downloads the update, then (after <paramref name="beforeRestart"/> has stopped everything) restarts into it.</summary>
    public async Task InstallAsync(Func<Task> beforeRestart)
    {
        if (_found == null || Busy) return;
        Busy = true;
        try
        {
            var mgr = Manager();
            await mgr.DownloadUpdatesAsync(_found, p => Status = $"Downloading {Available}… {p}%");
            Status = "Restarting to update…";
            await beforeRestart();
            mgr.ApplyUpdatesAndRestart(_found);
        }
        catch (Exception e)
        {
            Status = $"The update didn't install: {e.Message}";
            Busy = false;
        }
    }

    /// <summary>After switching channel, forget what the old channel found.</summary>
    public void ChannelChanged()
    {
        _found = null;
        Available = null;
        Status = "";
    }

    static string? InstalledVersion()
    {
        try { return new UpdateManager(new GithubSource(RepoUrl, null, false)).CurrentVersion?.ToString(); }
        catch { return null; }
    }

    static string AssemblyVersion() =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "dev build";
}

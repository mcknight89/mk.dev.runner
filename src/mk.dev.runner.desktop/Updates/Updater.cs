using System.Reflection;
using System.Windows.Threading;
using mk.dev.runner.core;
using mk.dev.runner.desktop.ViewModels;
using Velopack;
using Velopack.Sources;

namespace mk.dev.runner.desktop.Updates
{
    public sealed class Updater : Observable
    {
        public const string RepoUrl = "https://github.com/mcknight89/mk.dev.runner";

        private const string DevChannel = "dev";
        private const string StableChannel = "win";
        private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

        private readonly AppSettings _settings;
        private readonly DispatcherTimer _timer;
        private UpdateInfo? _found;

        public Updater(AppSettings settings)
        {
            _settings = settings;
            IsInstalled = Installed();
            CurrentVersion = InstalledVersion() ?? AssemblyVersion();
            if (IsInstalled && CurrentVersion != _settings.LastVersion)
                NoteNewVersion();
            Status = IsInstalled ? "" : "Updates work once Dev Runner is installed with its Setup.exe.";
            _timer = new DispatcherTimer { Interval = CheckEvery };
            _timer.Tick += async (_, _) => await CheckAsync(manual: false);
        }

        public bool IsInstalled { get; }

        private void NoteNewVersion()
        {
            if (CurrentVersion.Contains("-dev"))
                _settings.UpdateChannel = DevChannel;
            _settings.LastVersion = CurrentVersion;
            try
            {
                _settings.Save();
            }
            catch (IOException)
            {
            }
        }

        private static readonly Lazy<UpdateManager?> _installation = new(() =>
        {
            try
            {
                return new UpdateManager(new GithubSource(RepoUrl, null, false));
            }
            catch
            {
                return null;
            }
        });

        public static bool Installed()
        {
            return _installation.Value?.IsInstalled == true;
        }

        public bool WantsDev => _settings.UpdateChannel is { } chosen ? chosen == DevChannel : CurrentVersion.Contains("-dev");
        public string CurrentVersion { get; }

        private string _status = "";
        public string Status { get => _status; private set => Set(ref _status, value); }

        private string _statusDetail = "";
        public string StatusDetail { get => _statusDetail; private set => Set(ref _statusDetail, value); }

        private string? _available;
        public string? Available
        {
            get => _available; private set
            {
                if (Set(ref _available, value))
                    Raise(nameof(HasUpdate));
            }
        }
        public bool HasUpdate => Available != null;

        private bool _busy;
        public bool Busy { get => _busy; private set => Set(ref _busy, value); }

        public void Start()
        {
            if (!IsInstalled)
                return;
            var first = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            first.Tick += async (_, _) => { first.Stop(); await CheckAsync(manual: false); };
            first.Start();
            _timer.Start();
        }

        private UpdateManager Manager()
        {
            var dev = WantsDev;
            var switching = dev != CurrentVersion.Contains("-dev");
            var source = new GithubSource(RepoUrl, accessToken: null, prerelease: dev);
            var options = new UpdateOptions { ExplicitChannel = dev ? DevChannel : StableChannel, AllowVersionDowngrade = switching };
            return new UpdateManager(source, options);
        }

        public async Task CheckAsync(bool manual)
        {
            if (!IsInstalled || Busy)
                return;
            if (!manual && !_settings.CheckForUpdates)
                return;
            Busy = true;
            Status = "Checking for updates…";
            StatusDetail = "";
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
                Status = Plain("Couldn't check for updates", e);
            }
            finally { Busy = false; }
        }

        public async Task InstallAsync(Func<Task> beforeRestart)
        {
            if (_found == null || Busy)
                return;
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
                Status = Plain("The update didn't install", e);
                Busy = false;
            }
        }

        public void ChannelChanged()
        {
            _found = null;
            Available = null;
            Status = "";
        }

        private static string? InstalledVersion()
        {
            return Installed() ? _installation.Value!.CurrentVersion?.ToString() : null;
        }

        private static string AssemblyVersion()
        {
            return Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? "dev build";
        }

        private string Plain(string what, Exception e)
        {
            StatusDetail = e.Message;
            return e switch
            {
                System.Net.Http.HttpRequestException => $"{what}: couldn't reach GitHub. Check your connection and try again.",
                TaskCanceledException => $"{what}: GitHub took too long to answer. Try again.",
                IOException => $"{what}: a file couldn't be written. Try again after closing other copies of Dev Runner.",
                _ => $"{what}. Hover for details.",
            };
        }
    }
}

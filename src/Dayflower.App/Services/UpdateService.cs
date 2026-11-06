// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Velopack;
using Velopack.Sources;

namespace Dayflower.Services;

/// <summary>Where an installed Dayflower stands with updates.</summary>
public enum UpdateStatus
{
    /// <summary>A development build or the MSIX package: updates come some other way.</summary>
    NotAvailable,
    UpToDate,
    Checking,
    Downloading,

    /// <summary>Downloaded; applied the next time Dayflower starts, or at once with <see cref="UpdateService.RestartToUpdate"/>.</summary>
    Ready,
    Failed,
}

/// <summary>
/// Keeps an installed Dayflower up to date from GitHub Releases. Checks a minute after start and every six hours, and
/// downloads in the background. Dayflower rarely closes, so a downloaded update is applied the next time it starts
/// (usually at sign-in), when it's quit, or at once from Settings.
/// </summary>
public sealed class UpdateService : IDisposable
{
    private const string Repository = "https://github.com/kstawi/dayflower";

    /// <summary>A folder or web address to take updates from instead of GitHub, for testing updates locally.</summary>
    private const string SourceOverride = "DAYFLOWER_UPDATE_SOURCE";

    private static readonly TimeSpan FirstCheck = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    private readonly UpdateManager? _manager;
    private readonly DispatcherQueue _ui;
    private readonly ILogger<UpdateService> _logger;
    private readonly DispatcherQueueTimer? _timer;
    private VelopackAsset? _ready;

    public UpdateService(DispatcherQueue ui, ILogger<UpdateService> logger)
    {
        _ui = ui;
        _logger = logger;
        _manager = CreateManager();
        if (_manager is null)
        {
            return;
        }

        _ready = _manager.UpdatePendingRestart;
        Status = _ready is null ? UpdateStatus.UpToDate : UpdateStatus.Ready;
        _timer = ui.CreateTimer();
        _timer.Interval = FirstCheck;
        _timer.Tick += async (_, _) =>
        {
            _timer.Interval = Interval;
            await CheckAsync();
        };
        _timer.Start();
    }

    /// <summary>Raised on the UI thread when <see cref="Status"/> or <see cref="ReadyVersion"/> changes.</summary>
    public event EventHandler? Changed;

    public UpdateStatus Status { get; private set; } = UpdateStatus.NotAvailable;

    /// <summary>The version that's downloaded and ready, if any.</summary>
    public string? ReadyVersion => _ready?.Version?.ToString();

    public DateTimeOffset? LastChecked { get; private set; }

    /// <summary>
    /// Applies an update that was downloaded earlier and starts the new version, hidden again if this start was
    /// (at sign-in). Called at start-up, before anything else; returns false, and Dayflower starts as usual, when
    /// there's nothing to apply. Otherwise the process ends here.
    /// </summary>
    public static bool ApplyPendingUpdate(bool background)
    {
        try
        {
            if (CreateManager() is { UpdatePendingRestart: { } pending } manager)
            {
                manager.ApplyUpdatesAndRestart(pending, background ? [StartupService.BackgroundArgument] : []);
                return true;
            }
        }
        catch (Exception)
        {
            // Start the current version; the update is tried again next time.
        }

        return false;
    }

    /// <summary>Checks for an update now and downloads it.</summary>
    public async Task CheckAsync()
    {
        if (_manager is null || Status is UpdateStatus.Checking or UpdateStatus.Downloading)
        {
            return;
        }

        try
        {
            SetStatus(UpdateStatus.Checking);
            var update = await _manager.CheckForUpdatesAsync();
            LastChecked = DateTimeOffset.Now;
            if (update is null)
            {
                SetStatus(_ready is null ? UpdateStatus.UpToDate : UpdateStatus.Ready);
                return;
            }

            SetStatus(UpdateStatus.Downloading);
            await _manager.DownloadUpdatesAsync(update);
            _ready = update.TargetFullRelease;
            _logger.LogInformation("Downloaded update {Version}", ReadyVersion);
            SetStatus(UpdateStatus.Ready);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Update check failed");
            SetStatus(_ready is null ? UpdateStatus.Failed : UpdateStatus.Ready);
        }
    }

    /// <summary>Closes Dayflower, applies the downloaded update and starts the new version.</summary>
    public void RestartToUpdate()
    {
        if (_manager is not null && _ready is not null)
        {
            _logger.LogInformation("Restarting to update to {Version}", ReadyVersion);
            _manager.ApplyUpdatesAndRestart(_ready);
        }
    }

    /// <summary>On Quit: has a downloaded update applied once Dayflower has exited, without starting it again.</summary>
    public void ApplyAfterExit()
    {
        if (_manager is not null && _ready is not null)
        {
            _logger.LogInformation("Applying update {Version} after exit", ReadyVersion);
            _manager.WaitExitThenApplyUpdates(_ready, silent: true, restart: false);
        }
    }

    public void Dispose() => _timer?.Stop();

    /// <summary>The update manager for an installed copy, or null for a development build or the MSIX package.</summary>
    private static UpdateManager? CreateManager()
    {
        if (AppIdentity.IsPackaged || AppIdentity.InstalledAppId is null)
        {
            return null;
        }

        var manager = Environment.GetEnvironmentVariable(SourceOverride) is { Length: > 0 } source
            ? new UpdateManager(source)
            : new UpdateManager(new GithubSource(Repository, accessToken: null, prerelease: false));
        return manager.IsInstalled ? manager : null;
    }

    private void SetStatus(UpdateStatus status)
    {
        if (!_ui.HasThreadAccess)
        {
            _ui.TryEnqueue(() => SetStatus(status));
            return;
        }

        Status = status;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

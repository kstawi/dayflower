// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Windows.Networking.Connectivity;

namespace Dayflower.Services;

/// <summary>
/// Decides when to sync: at start-up, every two minutes, when the window comes back to the front, when the
/// network returns, and on request (e.g. right after an account is added). Runs one sync at a time in the
/// background; requests that arrive meanwhile are folded into one more run.
/// </summary>
public sealed class SyncCoordinator : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ActivationThrottle = TimeSpan.FromSeconds(30);

    private readonly SyncEngine _engine;
    private readonly DispatcherQueue _ui;
    private readonly ILogger<SyncCoordinator> _logger;
    private readonly DispatcherQueueTimer _timer;
    private readonly CancellationTokenSource _shutdown = new();
    private Task _running = Task.CompletedTask;
    private bool _pending;

    public SyncCoordinator(SyncEngine engine, DispatcherQueue ui, ILogger<SyncCoordinator> logger)
    {
        _engine = engine;
        _ui = ui;
        _logger = logger;
        _timer = ui.CreateTimer();
        _timer.Interval = Interval;
        _timer.Tick += (_, _) => Request();
    }

    /// <summary>Raised on the UI thread when a sync starts.</summary>
    public event EventHandler? Started;

    /// <summary>Raised on the UI thread when a sync finishes.</summary>
    public event EventHandler<SyncReport>? Completed;

    public bool IsSyncing { get; private set; }

    public DateTimeOffset? LastCompleted { get; private set; }

    public SyncReport? LastReport { get; private set; }

    public void Start()
    {
        _timer.Start();
        NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        Request();
    }

    /// <summary>Asks for a sync soon; returns when it (or the run it was folded into) has finished.</summary>
    public Task SyncNowAsync()
    {
        Request();
        return _running;
    }

    public void OnWindowActivated()
    {
        // Not while a sync is running or before the first one has finished: start-up already syncs.
        if (!IsSyncing && LastCompleted is { } last && DateTimeOffset.UtcNow - last > ActivationThrottle)
        {
            Request();
        }
    }

    public void Dispose()
    {
        NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        _timer.Stop();
        _shutdown.Cancel();
    }

    private void Request()
    {
        if (IsSyncing)
        {
            _pending = true;
            return;
        }

        _running = RunAsync();
    }

    private async Task RunAsync()
    {
        IsSyncing = true;
        Started?.Invoke(this, EventArgs.Empty);
        try
        {
            do
            {
                _pending = false;
                try
                {
                    var report = await Task.Run(() => _engine.SyncAllAsync(_shutdown.Token));
                    LastReport = report;
                    LastCompleted = DateTimeOffset.UtcNow;
                    Completed?.Invoke(this, report);
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sync run failed");
                }
            }
            while (_pending);
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private void OnNetworkStatusChanged(object sender)
    {
        var level = NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel();
        if (level == NetworkConnectivityLevel.InternetAccess)
        {
            _ui.TryEnqueue(Request);
        }
    }
}

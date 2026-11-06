// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Services;
using Dayflower.Views;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;

namespace Dayflower;

/// <summary>
/// Dayflower keeps running in the notification area: closing the window hides it, reminders keep coming, and Quit
/// in the tray menu ends it. Started by Windows at sign-in it starts hidden.
/// </summary>
public partial class App : Application
{
    private AppServices? _services;
    private AppSettings? _settings;
    private MainWindow? _window;
    private TrayService? _tray;
    private NotificationService? _notifications;
    private ReminderScheduler? _reminders;
    private ILogger<App>? _logger;
    private DispatcherQueue? _ui;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) => _logger?.LogCritical(e.Exception, "Unhandled exception");
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _ui = DispatcherQueue.GetForCurrentThread();
        _services = AppServices.Create(_ui);
        _logger = _services.Loggers.CreateLogger<App>();
        _settings = AppSettings.Load();

        // Notification clicks while running arrive here; the handler must be set before registering.
        _notifications = new NotificationService(_services.Loggers.CreateLogger<NotificationService>());
        _notifications.ActionInvoked += (_, action) => _ui.TryEnqueue(async () => await HandleNotificationAsync(action));
        _notifications.Register();

        var join = new JoinService(_services.Loggers.CreateLogger<JoinService>());
        _reminders = new ReminderScheduler(_services, _settings, _notifications, join, _ui);
        _reminders.Ticked += (_, _) => UpdateTrayToolTip();

        _window = new MainWindow(_services, _settings, join);
        _window.HiddenToTray += (_, _) => ShowTrayNoticeOnce();
        _window.Accounts.CalendarsChanged += async (_, _) => await _reminders.ReplanAsync();

        _tray = new TrayService(_services.Loggers.CreateLogger<TrayService>());
        _tray.OpenRequested += (_, _) => _window.ShowWindow();
        _tray.SyncRequested += async (_, _) => await _services.Sync.SyncNowAsync();
        _tray.QuitRequested += (_, _) => Quit();
        _tray.StartupBlocked += (_, text) => _notifications.ShowNotice("Start with Windows", text);

        // A second launch (Start menu, startup task, notification) is redirected here by Program.Main.
        AppInstance.GetCurrent().Activated += (_, e) => _ui.TryEnqueue(() => OnActivated(e));

        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        bool startedByWindows = activation.Kind == ExtendedActivationKind.StartupTask;
        if (!startedByWindows)
        {
            _window.ShowWindow();
        }

        _logger.LogInformation("Started ({Kind})", activation.Kind);
        _services.Sync.Completed += async (_, _) => await _reminders.ReplanAsync();
        _services.Sync.Start();
        await _reminders.StartAsync();

        if (activation.Kind == ExtendedActivationKind.AppNotification && activation.Data is AppNotificationActivatedEventArgs launchedBy)
        {
            await HandleNotificationAsync(NotificationService.Parse(launchedBy));
        }
    }

    private async void OnActivated(AppActivationArguments e)
    {
        if (e.Kind == ExtendedActivationKind.AppNotification && e.Data is AppNotificationActivatedEventArgs notification)
        {
            await HandleNotificationAsync(NotificationService.Parse(notification));
        }
        else if (e.Kind != ExtendedActivationKind.StartupTask)
        {
            _window?.ShowWindow();
        }
    }

    private async Task HandleNotificationAsync(NotificationAction action)
    {
        switch (action.Action)
        {
            case "join":
            case "snooze":
                await _reminders!.HandleAsync(action);
                break;
            case "open":
                _window!.ShowWindow();
                if (_reminders!.Find(action.Id) is { } item)
                {
                    _window.ShowDate(item.Span.FirstDay);
                }

                break;
        }
    }

    private void UpdateTrayToolTip()
    {
        if (_tray is null || _reminders is null)
        {
            return;
        }

        var next = _reminders.NextEvent;
        if (next is null)
        {
            _tray.SetToolTip("Dayflower");
            return;
        }

        var minutes = (next.Span.Start - DateTime.Now).TotalMinutes;
        string when = minutes < 60 ? $"in {Math.Max(1, (int)Math.Round(minutes))} min"
            : DateOnly.FromDateTime(next.Span.Start) == DateOnly.FromDateTime(DateTime.Now) ? $"at {TimeText.Time(next.Span.Start)}"
            : $"tomorrow at {TimeText.Time(next.Span.Start)}";
        _tray.SetToolTip($"Dayflower\nNext: {next.Title} {when}");
    }

    private void ShowTrayNoticeOnce()
    {
        if (_settings is { TrayNoticeShown: false })
        {
            _settings.TrayNoticeShown = true;
            _settings.Save();
            _notifications?.ShowNotice("Dayflower is still running",
                "It stays in the notification area to remind you about events. To quit, right-click its icon.");
        }
    }

    private void Quit()
    {
        _logger?.LogInformation("Quitting");
        _reminders?.Dispose();
        _tray?.Dispose();
        _notifications?.Unregister();
        _window?.CloseForGood();
        _services?.Dispose();
        Exit();
    }
}

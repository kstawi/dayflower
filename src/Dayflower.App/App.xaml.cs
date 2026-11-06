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
/// Dayflower keeps running in the background: closing the window hides it and reminders keep coming; opening it from
/// Start brings the window back, and Quit in Settings ends it. Started by Windows at sign-in it starts hidden.
/// </summary>
public partial class App : Application
{
    private AppServices? _services;
    private AppSettings? _settings;
    private MainWindow? _window;
    private NotificationService? _notifications;
    private ReminderScheduler? _reminders;
    private InvitationWatcher? _invitations;
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

        var edits = new EventEditService(_services);
        _invitations = new InvitationWatcher(_services, _settings, _notifications, edits, _services.Loggers.CreateLogger<InvitationWatcher>());
        _settings.Changed += async (_, _) => await _reminders.ReplanAsync();

        _window = new MainWindow(_services, _settings, join, edits);
        _window.HiddenToBackground += (_, _) => ShowBackgroundNoticeOnce();
        _window.QuitRequested += (_, _) => Quit();
        _window.Accounts.CalendarsChanged += async (_, _) => await _reminders.ReplanAsync();

        // A second launch (Start menu, startup task, notification) is redirected here by Program.Main.
        AppInstance.GetCurrent().Activated += (_, e) => _ui.TryEnqueue(() => OnActivated(e));

        // Started at sign-in, it stays hidden, unless it isn't to run in the background (then closing quits it).
        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (!StartedByWindows(activation) || !_settings.RunInBackground)
        {
            _window.ShowWindow();
        }

        _logger.LogInformation("Started ({Kind})", activation.Kind);
        _services.Sync.Completed += async (_, _) =>
        {
            await _reminders.ReplanAsync();
            await _invitations.CheckAsync();
        };
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
        else if (!StartedByWindows(e))
        {
            _window?.ShowWindow();
        }
    }

    /// <summary>
    /// Started at sign-in: by the package's startup task, or (as a plain program) by the Run entry, which passes
    /// <see cref="StartupService.BackgroundArgument"/>.
    /// </summary>
    private static bool StartedByWindows(AppActivationArguments activation) =>
        activation.Kind == ExtendedActivationKind.StartupTask
        || (activation.Kind == ExtendedActivationKind.Launch
            && activation.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch
            && launch.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(StartupService.BackgroundArgument));

    private async Task HandleNotificationAsync(NotificationAction action)
    {
        switch (action.Action)
        {
            case "join":
            case "snooze":
                await _reminders!.HandleAsync(action);
                break;
            case "rsvp":
                await _invitations!.RespondAsync(action.Id, action.Response);
                break;
            case "invite":
                _window!.ShowWindow();
                if (await _invitations!.FindDayAsync(action.Id) is { } day)
                {
                    _window.ShowDate(day);
                }

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

    private void ShowBackgroundNoticeOnce()
    {
        if (_settings is { BackgroundNoticeShown: false })
        {
            _settings.BackgroundNoticeShown = true;
            _settings.Save();
            _notifications?.ShowNotice("Dayflower is still running",
                "It keeps reminding you about events in the background. Open it from Start; to quit, use Quit in Settings.");
        }
    }

    private void Quit()
    {
        _logger?.LogInformation("Quitting");
        _services?.Updates.ApplyAfterExit();
        _reminders?.Dispose();
        _notifications?.Unregister();
        _window?.CloseForGood();
        _services?.Dispose();
        Exit();
    }
}

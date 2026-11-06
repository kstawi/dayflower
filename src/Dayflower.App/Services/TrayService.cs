// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel;

namespace Dayflower.Services;

/// <summary>
/// The notification-area icon: left-click opens Dayflower; right-click offers Open, New event, Sync now, Settings,
/// Start with Windows and Quit. The tooltip shows the next event.
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly TaskbarIcon _icon;
    private readonly ToggleMenuFlyoutItem _startup;
    private readonly ILogger _logger;
    private string _toolTip = "Dayflower";

    public TrayService(ILogger<TrayService> logger)
    {
        _logger = logger;

        var open = new MenuFlyoutItem { Text = "Open Dayflower", Icon = new FontIcon { Glyph = "" } };
        open.Click += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
        var sync = new MenuFlyoutItem { Text = "Sync now", Icon = new FontIcon { Glyph = "" } };
        sync.Click += (_, _) => SyncRequested?.Invoke(this, EventArgs.Empty);
        var newEvent = new MenuFlyoutItem { Text = "New event", Icon = new FontIcon { Glyph = "" } };
        newEvent.Click += (_, _) => NewEventRequested?.Invoke(this, EventArgs.Empty);
        var settings = new MenuFlyoutItem { Text = "Settings", Icon = new FontIcon { Glyph = "" } };
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _startup = new ToggleMenuFlyoutItem { Text = "Start with Windows" };
        _startup.Click += async (_, _) => await SetStartupAsync(_startup.IsChecked);
        var quit = new MenuFlyoutItem { Text = "Quit Dayflower", Icon = new FontIcon { Glyph = "" } };
        quit.Click += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);

        var menu = new MenuFlyout();
        menu.Items.Add(open);
        menu.Items.Add(newEvent);
        menu.Items.Add(sync);
        menu.Items.Add(settings);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(_startup);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(quit);
        menu.Opening += async (_, _) => _startup.IsChecked = await StartupService.IsEnabledAsync();

        _icon = new TaskbarIcon
        {
            ToolTipText = _toolTip,
            IconSource = new BitmapImage(new Uri("ms-appx:///Assets/app.ico")),
            ContextMenuMode = ContextMenuMode.SecondWindow,
            ContextFlyout = menu,
            NoLeftClickDelay = true,
            LeftClickCommand = new RelayCommand(() => OpenRequested?.Invoke(this, EventArgs.Empty)),
        };

        // Not efficiency mode: reminders have to fire on time while the window is hidden.
        _icon.ForceCreate(enablesEfficiencyMode: false);
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? SyncRequested;

    public event EventHandler? NewEventRequested;

    public event EventHandler? SettingsRequested;

    public event EventHandler? QuitRequested;

    /// <summary>Notice shown when Windows won't let Dayflower turn start-up back on itself.</summary>
    public event EventHandler<string>? StartupBlocked;

    public void SetToolTip(string text)
    {
        // The notification area cuts tooltips off at 127 characters.
        text = text.Length > 127 ? text[..126] + "…" : text;
        if (text != _toolTip)
        {
            _toolTip = text;
            _icon.ToolTipText = text;
        }
    }

    public void Dispose() => _icon.Dispose();

    private async Task SetStartupAsync(bool enable)
    {
        try
        {
            var state = await StartupService.SetEnabledAsync(enable);
            _logger.LogInformation("Start with Windows: {State}", state);
            if (enable && state == StartupTaskState.DisabledByUser)
            {
                StartupBlocked?.Invoke(this, "Windows only lets you turn this back on yourself: Task Manager › Startup apps › Dayflower › Enable.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Changing start with Windows failed");
        }
    }
}

/// <summary>Dayflower's startup task (declared in the package manifest, on by default).</summary>
public static class StartupService
{
    private const string TaskId = "DayflowerStartup";

    public static async Task<bool> IsEnabledAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(TaskId);
            return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        }
        catch (Exception)
        {
            return false; // not packaged
        }
    }

    /// <summary>The startup task's state, or null when Dayflower isn't running as an installed package.</summary>
    public static async Task<StartupTaskState?> GetStateAsync()
    {
        try
        {
            return (await StartupTask.GetAsync(TaskId)).State;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<StartupTaskState> SetEnabledAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(TaskId);
        if (enabled)
        {
            return await task.RequestEnableAsync();
        }

        task.Disable();
        return task.State;
    }
}

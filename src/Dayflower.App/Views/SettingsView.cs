// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Collections.Specialized;
using System.Globalization;
using Dayflower.Calendar;
using Dayflower.Services;
using Dayflower.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;

namespace Dayflower.Views;

/// <summary>
/// The Settings page, in the style of Windows Settings: calendar defaults, notifications, accounts, appearance,
/// start-up, keyboard shortcuts and about. Changes are saved and applied at once.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    /// <summary>The keyboard shortcuts, also listed here for reference.</summary>
    public static readonly (string Keys, string Action)[] Shortcuts =
    [
        ("Ctrl+N", "New event"),
        ("Ctrl+F", "Search"),
        ("T", "Go to today"),
        ("D, W, M, A", "Day, week, month or agenda view"),
        ("Page Up, Page Down", "Previous or next day, week or month (also ← and →)"),
        ("F5", "Sync now"),
        ("Ctrl+,", "Settings"),
        ("Esc", "Back to the calendar from search or settings"),
    ];

    private static readonly int[] JoinLeads = [0, 2, 5, 10, 15];

    private readonly AppSettings _settings;
    private readonly AccountsViewModel _accounts;
    private readonly EventActions _actions;
    private readonly StackPanel _accountCards = new() { Spacing = 4 };
    private readonly ComboBox _defaultCalendar = new() { MinWidth = 240 };
    private readonly ToggleSwitch _startup = new();
    private readonly TextBlock _startupNote = Description(string.Empty);

    public SettingsView(AppSettings settings, AccountsViewModel accounts, EventActions actions)
    {
        _settings = settings;
        _accounts = accounts;
        _actions = actions;

        var back = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 },
            Style = EventVisuals.Resource<Style>("SubtleIconButtonStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(back, "Back to the calendar");
        ToolTipService.SetToolTip(back, "Back (Esc)");
        back.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        var title = new TextBlock { Text = "Settings", Style = EventVisuals.Resource<Style>("SubtitleTextBlockStyle"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(12, 12, 16, 8) };
        header.Children.Add(back);
        header.Children.Add(title);

        var page = new StackPanel { Spacing = 4, MaxWidth = 1000, Padding = new Thickness(24, 0, 24, 24), HorizontalAlignment = HorizontalAlignment.Stretch };
        BuildCalendar(page);
        BuildNotifications(page);
        BuildAccounts(page);
        BuildAppearance(page);
        BuildGeneral(page);
        BuildAbout(page);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(header);
        var scroller = new ScrollViewer { Content = page };
        Grid.SetRow(scroller, 1);
        root.Children.Add(scroller);
        Content = root;

        Loaded += async (_, _) => await LoadAsync();
        accounts.Accounts.CollectionChanged += OnAccountsChanged;
        Unloaded += (_, _) => accounts.Accounts.CollectionChanged -= OnAccountsChanged;
    }

    /// <summary>Raised when the user goes back to the calendar.</summary>
    public event EventHandler? CloseRequested;

    // ---- Sections --------------------------------------------------------------------------------------------

    private void BuildCalendar(StackPanel page)
    {
        Section(page, "Calendar");
        _defaultCalendar.SelectionChanged += (_, _) =>
        {
            if (_defaultCalendar.SelectedItem is ComboBoxItem { Tag: var tag } && tag as string != _settings.DefaultCalendar)
            {
                _settings.DefaultCalendar = tag as string;
                _settings.SaveAndApply();
            }
        };
        page.Children.Add(Card("", "Default calendar for new events", "New events start in this calendar; you can pick another for each.", _defaultCalendar));

        var start = HourBox(_settings.WorkdayStartHour, 0, 23);
        var end = HourBox(_settings.WorkdayEndHour, 1, 24);
        start.SelectionChanged += (_, _) =>
        {
            int hour = (int)((ComboBoxItem)start.SelectedItem).Tag;
            if (hour < _settings.WorkdayEndHour)
            {
                _settings.WorkdayStartHour = hour;
                _settings.SaveAndApply();
            }
        };
        end.SelectionChanged += (_, _) =>
        {
            int hour = (int)((ComboBoxItem)end.SelectedItem).Tag;
            if (hour > _settings.WorkdayStartHour)
            {
                _settings.WorkdayEndHour = hour;
                _settings.SaveAndApply();
            }
        };
        AutomationProperties.SetName(start, "Working hours start");
        AutomationProperties.SetName(end, "Working hours end");
        var hours = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        hours.Children.Add(start);
        hours.Children.Add(new TextBlock { Text = "to", VerticalAlignment = VerticalAlignment.Center });
        hours.Children.Add(end);
        page.Children.Add(Card("", "Working hours", "Time outside these hours is shaded in the day and week views.", hours));

        page.Children.Add(Card("", "Show declined events", "Invitations you declined appear struck through instead of hidden.",
            Toggle(_settings.ShowDeclinedEvents, on => _settings.ShowDeclinedEvents = on)));
    }

    private void BuildNotifications(StackPanel page)
    {
        Section(page, "Notifications");
        page.Children.Add(Card("", "Keep reminders on screen", "Reminders stay until you dismiss or snooze them, like Windows' own reminders.",
            Toggle(_settings.KeepRemindersOnScreen, on => _settings.KeepRemindersOnScreen = on)));
        page.Children.Add(Card("", "“Starting now” alert", "A second notification with Join when a meeting starts.",
            Toggle(_settings.StartingNowAlert, on => _settings.StartingNowAlert = on)));
        page.Children.Add(Card("", "New invitations", "A notification with Accept, Maybe and Decline when someone invites you.",
            Toggle(_settings.InvitationNotifications, on => _settings.InvitationNotifications = on)));

        var lead = new ComboBox { MinWidth = 200 };
        foreach (int minutes in JoinLeads)
        {
            lead.Items.Add(new ComboBoxItem { Content = minutes == 0 ? "When it starts" : $"{minutes} minutes before", Tag = minutes });
        }

        lead.SelectedItem = lead.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == _settings.JoinLeadMinutes)
                            ?? lead.Items.OfType<ComboBoxItem>().First(i => (int)i.Tag == 10);
        lead.SelectionChanged += (_, _) =>
        {
            _settings.JoinLeadMinutes = (int)((ComboBoxItem)lead.SelectedItem).Tag;
            _settings.SaveAndApply();
        };
        page.Children.Add(Card("", "Join button on events", "When the Join button appears on meetings in the day and week views.", lead));
    }

    private void BuildAccounts(StackPanel page)
    {
        Section(page, "Accounts");
        page.Children.Add(_accountCards);
        RefreshAccounts();
    }

    private void BuildAppearance(StackPanel page)
    {
        Section(page, "Appearance");
        var theme = new ComboBox { MinWidth = 200 };
        theme.Items.Add(new ComboBoxItem { Content = "Use Windows setting", Tag = AppTheme.System });
        theme.Items.Add(new ComboBoxItem { Content = "Light", Tag = AppTheme.Light });
        theme.Items.Add(new ComboBoxItem { Content = "Dark", Tag = AppTheme.Dark });
        theme.SelectedItem = theme.Items.OfType<ComboBoxItem>().First(i => (AppTheme)i.Tag == _settings.Theme);
        theme.SelectionChanged += (_, _) =>
        {
            _settings.Theme = (AppTheme)((ComboBoxItem)theme.SelectedItem).Tag;
            _settings.SaveAndApply();
        };
        page.Children.Add(Card("", "Theme", "Dayflower follows Windows' light or dark mode unless you pick one here.", theme));
    }

    private void BuildGeneral(StackPanel page)
    {
        Section(page, "General");
        _startup.Toggled += async (_, _) => await SetStartupAsync(_startup.IsOn);
        page.Children.Add(Card("", "Start with Windows", "Dayflower starts in the notification area when you sign in, so reminders arrive.", _startup, _startupNote));

        var keys = new Grid { ColumnSpacing = 24, RowSpacing = 6, Margin = new Thickness(0, 10, 0, 4) };
        keys.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        keys.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < Shortcuts.Length; i++)
        {
            keys.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var key = new TextBlock { Text = Shortcuts[i].Keys, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            var action = new TextBlock { Text = Shortcuts[i].Action, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(key, i);
            Grid.SetRow(action, i);
            Grid.SetColumn(action, 1);
            keys.Children.Add(key);
            keys.Children.Add(action);
        }

        page.Children.Add(Card("", "Keyboard shortcuts", "Letter shortcuts work when you aren't typing in a box.", null, keys));
    }

    private void BuildAbout(StackPanel page)
    {
        Section(page, "About");
        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        links.Children.Add(Link("Source code", "https://github.com/kstawi/dayflower"));
        links.Children.Add(Link("Privacy", "https://github.com/kstawi/dayflower/blob/main/PRIVACY.md"));
        links.Children.Add(Link("Licence", "https://github.com/kstawi/dayflower/blob/main/LICENSE"));
        var logs = new HyperlinkButton { Content = "Open logs folder" };
        logs.Click += async (_, _) => await Windows.System.Launcher.LaunchFolderPathAsync(AppPaths.LogsFolder);
        links.Children.Add(logs);
        page.Children.Add(Card("", "Dayflower", $"Version {Version()} · Free software under the GNU GPL 3.0. Your events stay between this PC and Google.", null, links));
    }

    // ---- Accounts --------------------------------------------------------------------------------------------

    private void OnAccountsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshAccounts();

    private void RefreshAccounts()
    {
        _accountCards.Children.Clear();
        foreach (var account in _accounts.Accounts)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var signIn = new Button { Content = "Sign in again", Command = account.SignInAgainCommand };
            var remove = new Button { Content = "Remove", Command = account.RemoveCommand };
            AutomationProperties.SetName(signIn, $"Sign in to {account.Email} again");
            AutomationProperties.SetName(remove, $"Remove {account.Email}");
            buttons.Children.Add(signIn);
            buttons.Children.Add(remove);
            string state = account.NeedsSignIn ? "Google stopped accepting the saved sign-in. Sign in again."
                : $"Google account · {account.Calendars.Count} {(account.Calendars.Count == 1 ? "calendar" : "calendars")}";
            _accountCards.Children.Add(Card("", account.Email, state, buttons));
        }

        var add = new Button { Content = "Add account", Command = _accounts.AddAccountCommand, Style = EventVisuals.Resource<Style>("AccentButtonStyle") };
        _accountCards.Children.Add(Card("", "Add a Google account", "Personal and Workspace accounts can be used side by side.", add));
    }

    // ---- Loading ---------------------------------------------------------------------------------------------

    private async Task LoadAsync()
    {
        var calendars = await _actions.GetWritableCalendarsAsync();
        _defaultCalendar.Items.Clear();
        _defaultCalendar.Items.Add(new ComboBoxItem { Content = "The last one used", Tag = null });
        foreach (var calendar in calendars)
        {
            _defaultCalendar.Items.Add(new ComboBoxItem { Content = calendar.ToString(), Tag = $"{calendar.AccountId}|{calendar.Id}" });
        }

        _defaultCalendar.SelectedItem = _defaultCalendar.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag as string == _settings.DefaultCalendar)
                                        ?? _defaultCalendar.Items[0];

        var state = await StartupService.GetStateAsync();
        _updatingStartup = true;
        _startup.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        _startup.IsEnabled = state is StartupTaskState.Enabled or StartupTaskState.Disabled;
        _startupNote.Text = state switch
        {
            StartupTaskState.DisabledByUser => "Turned off in Task Manager or Windows Settings (Apps > Startup). Turn it on there.",
            StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy => "Set by your organisation.",
            null => "Available when Dayflower is installed.",
            _ => string.Empty,
        };
        _startupNote.Visibility = _startupNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _updatingStartup = false;
    }

    private bool _updatingStartup;

    private async Task SetStartupAsync(bool on)
    {
        if (_updatingStartup)
        {
            return;
        }

        try
        {
            await StartupService.SetEnabledAsync(on);
        }
        catch (Exception)
        {
            // The state shown below says what happened.
        }

        await LoadAsync();
    }

    // ---- Building blocks -------------------------------------------------------------------------------------

    private static void Section(StackPanel page, string title)
    {
        var text = new TextBlock { Text = title, Style = EventVisuals.Resource<Style>("BodyStrongTextBlockStyle"), Margin = new Thickness(0, 20, 0, 4) };
        AutomationProperties.SetHeadingLevel(text, AutomationHeadingLevel.Level2);
        page.Children.Add(text);
    }

    /// <summary>A setting card: icon, title and description on the left, the control on the right, details below.</summary>
    private static Border Card(string glyph, string header, string description, FrameworkElement? control, UIElement? below = null)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        grid.Children.Add(new FontIcon { Glyph = glyph, FontSize = 20, VerticalAlignment = VerticalAlignment.Center });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = header, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(Description(description));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (control is not null)
        {
            control.VerticalAlignment = VerticalAlignment.Center;
            if (AutomationProperties.GetName(control) is not { Length: > 0 })
            {
                AutomationProperties.SetName(control, header);
            }

            Grid.SetColumn(control, 2);
            grid.Children.Add(control);
        }

        if (below is FrameworkElement extra)
        {
            Grid.SetRow(extra, 1);
            Grid.SetColumn(extra, 1);
            Grid.SetColumnSpan(extra, 2);
            grid.Children.Add(extra);
        }

        return new Border { Style = EventVisuals.Resource<Style>("SettingsCardStyle"), Child = grid };
    }

    private static TextBlock Description(string text) => new() { Text = text, Style = EventVisuals.Resource<Style>("SettingsDescriptionTextStyle") };

    private ToggleSwitch Toggle(bool value, Action<bool> set)
    {
        var toggle = new ToggleSwitch { IsOn = value, OnContent = "On", OffContent = "Off", MinWidth = 0 };
        toggle.Toggled += (_, _) =>
        {
            set(toggle.IsOn);
            _settings.SaveAndApply();
        };
        return toggle;
    }

    private static ComboBox HourBox(int selected, int from, int to)
    {
        var box = new ComboBox { MinWidth = 110 };
        for (int hour = from; hour <= to; hour++)
        {
            var item = new ComboBoxItem { Content = hour == 24 ? "Midnight" : TimeText.Hour(hour), Tag = hour };
            box.Items.Add(item);
            if (hour == selected)
            {
                box.SelectedItem = item;
            }
        }

        return box;
    }

    private static HyperlinkButton Link(string text, string url) => new() { Content = text, NavigateUri = new Uri(url) };

    private static string Version()
    {
        try
        {
            var v = Package.Current.Id.Version;
            return string.Create(CultureInfo.InvariantCulture, $"{v.Major}.{v.Minor}.{v.Build}");
        }
        catch (InvalidOperationException)
        {
            return typeof(SettingsView).Assembly.GetName().Version?.ToString(3) ?? "?";
        }
    }
}

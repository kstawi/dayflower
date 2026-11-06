// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using Dayflower.Calendar;
using Dayflower.Core.Calendar;
using Dayflower.Core.Editing;
using Dayflower.Core.Model;
using Dayflower.Services;
using Dayflower.ViewModels;
using Dayflower.Views;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.System.UserProfile;

namespace Dayflower;

public sealed partial class MainWindow : Window
{
    private const int DefaultWidth = 1280;
    private const int DefaultHeight = 840;
    private const int MinimumWidth = 760;
    private const int MinimumHeight = 520;

    private readonly AppServices _services;
    private readonly CalendarDataService _data;
    private readonly AppSettings _settings;
    private readonly ILogger<MainWindow> _logger;
    private readonly JoinService _join;
    private readonly EventEditService _edits;
    private readonly EventActions _actions;

    // Set while the window updates the mini month or view switcher itself, so their change events don't loop back.
    private bool _updatingControls;

    // Each reload gets a number; a slower, older reload doesn't overwrite a newer one.
    private int _loadVersion;

    // Closing only hides the window, except when the app quits.
    private bool _closingForGood;

    // While a flyout is open, a background reload waits: rebuilding the view would pull the flyout's event (or the
    // outline of a new event) out from under it.
    private int _openFlyouts;
    private bool _reloadPending;

    public MainWindow(AppServices services, AppSettings settings, JoinService join, EventEditService edits)
    {
        _services = services;
        _settings = settings;
        _join = join;
        _data = new CalendarDataService(services, () => settings.ShowDeclinedEvents);
        _logger = services.Loggers.CreateLogger<MainWindow>();
        _edits = edits;
        _actions = new EventActions(services, _edits, settings, () => (FrameworkElement)Content);

        // Week start comes from Windows' regional settings (Settings > Time & language > Language & region).
        var weekStart = GlobalizationPreferences.WeekStartsOn;
        ViewModel = new ShellViewModel(
            new PeriodTitleFormatter(CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture),
            () => DateOnly.FromDateTime(DateTime.Now),
            (DayOfWeek)(int)weekStart);
        Accounts = new AccountsViewModel(services) { ConfirmAsync = ConfirmAsync };

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        // Tall caption buttons (48 px) line up with the pane button and the search box.
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppTitleBar.SizeChanged += (_, _) => CenterSearchBox();
        ShowTodayIcon();
        _iconClock = DispatcherQueue.CreateTimer();
        _iconClock.Interval = TimeSpan.FromMinutes(1);
        _iconClock.Tick += (_, _) => ShowTodayIcon();
        _iconClock.Start();
        ApplyTheme();
        SizeAndCenter();

        MiniMonth.FirstDayOfWeek = weekStart;
        WorldClockPanel.Initialize(settings);
        settings.Changed += (_, _) => OnSettingsChanged();
        Content.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(Root_KeyDown), handledEventsToo: false);
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateControlsFromPeriod();

        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                _services.Sync.OnWindowActivated();
            }
        };
        services.Sync.Completed += (_, _) => DataChanged();
        Accounts.CalendarsChanged += (_, _) => DataChanged();
        _edits.Changed += (_, _) => DataChanged();
        _actions.WorkingChanged += (_, working) => SavingBar.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        TimeGrid.CanMove = item => item.EditableCopy is not null;

        // Closing the window hides it; Dayflower keeps running in the background to remind about events.
        AppWindow.Closing += (_, args) =>
        {
            if (!_closingForGood)
            {
                args.Cancel = true;
                AppWindow.Hide();
                HiddenToBackground?.Invoke(this, EventArgs.Empty);
            }
        };
        _ = StartAsync();
    }

    /// <summary>Raised when closing the window hid it; Dayflower keeps running in the background.</summary>
    public event EventHandler? HiddenToBackground;

    /// <summary>Raised when the user chooses Quit in Settings.</summary>
    public event EventHandler? QuitRequested;

    /// <summary>Shows the window (from the tray, a notification or a second launch) and brings it to the front.</summary>
    public void ShowWindow()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        AppWindow.Show();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    /// <summary>Opens the day view on <paramref name="day"/>, e.g. for a clicked notification.</summary>
    public void ShowDate(DateOnly day) => ViewModel.ShowDay(day);

    /// <summary>Really closes the window, when the app quits.</summary>
    public void CloseForGood()
    {
        _closingForGood = true;
        Close();
    }

    private async Task StartAsync()
    {
        await Accounts.LoadAsync();
        await ReloadAsync(periodChanged: true);
    }

    public ShellViewModel ViewModel { get; }

    public AccountsViewModel Accounts { get; }

    public static bool Not(bool value) => !value;

    private async Task<bool> ConfirmAsync(string title, string text, string confirm)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = text,
            PrimaryButtonText = confirm,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void SizeAndCenter()
    {
        double scale = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(MinimumWidth * scale);
            presenter.PreferredMinimumHeight = (int)(MinimumHeight * scale);
        }

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min((int)(DefaultWidth * scale), workArea.Width);
        int height = Math.Min((int)(DefaultHeight * scale), workArea.Height);
        AppWindow.MoveAndResize(new RectInt32(
            workArea.X + (workArea.Width - width) / 2,
            workArea.Y + (workArea.Height - height) / 2,
            width,
            height));
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.Period))
        {
            // Going to another date or view leaves search.
            _searchQuery = null;
            SearchPanel.Visibility = Visibility.Collapsed;
            UpdateControlsFromPeriod();
            _ = ReloadAsync(periodChanged: true);
        }
    }

    /// <summary>The cache or the visible calendars changed: search starts afresh and the view reloads.</summary>
    private void DataChanged()
    {
        _data.InvalidateSearch();
        _ = ReloadAsync(periodChanged: false);
    }

    private void OnSettingsChanged()
    {
        ApplyTheme();
        DataChanged();
    }

    // ---- Search ----------------------------------------------------------------------------------------------

    // The query whose results are showing, or null for the calendar.
    private string? _searchQuery;
    private CancellationTokenSource? _suggestions;

    /// <summary>As the user types: the first few matches, newest search only.</summary>
    private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        _suggestions?.Cancel();
        var search = _suggestions = new CancellationTokenSource();
        string text = sender.Text.Trim();
        if (text.Length == 0)
        {
            sender.ItemsSource = null;
            return;
        }

        try
        {
            await Task.Delay(150, search.Token);
            var results = await Task.Run(() => _data.SearchAsync(text, search.Token), search.Token);
            if (!search.IsCancellationRequested)
            {
                sender.ItemsSource = results.Take(8).Select(i => new SearchSuggestion(i)).ToList();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed");
        }
    }

    /// <summary>Enter: all results in place of the calendar; a picked suggestion opens that event on its day.</summary>
    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _suggestions?.Cancel();
        if (args.ChosenSuggestion is SearchSuggestion chosen)
        {
            ViewModel.ShowDate(chosen.Item.Span.FirstDay);
            View_ItemClicked(this, new ItemClickedEventArgs(sender, chosen.Item));
            return;
        }

        if (args.QueryText.Trim() is { Length: > 0 } query)
        {
            sender.ItemsSource = null;
            _searchQuery = query;
            await ReloadAsync(periodChanged: false);
        }
    }

    private async Task ShowSearchResultsAsync(string query)
    {
        IReadOnlyList<CalendarItem> results;
        try
        {
            results = await Task.Run(() => _data.SearchAsync(query));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed");
            return;
        }

        if (_searchQuery != query)
        {
            return;
        }

        SearchTitle.Text = results.Count switch
        {
            0 => $"No events match “{query}”",
            1 => $"1 event matches “{query}”",
            _ => $"{results.Count} events match “{query}”",
        };
        SearchResults.ShowResults(results, "Try other words, or check that the calendar is shown in the side pane.");
        SearchPanel.Visibility = Visibility.Visible;
    }

    private void CloseSearchButton_Click(object sender, RoutedEventArgs e) => CloseSearch();

    private void CloseSearch()
    {
        if (_searchQuery is null)
        {
            return;
        }

        _searchQuery = null;
        SearchPanel.Visibility = Visibility.Collapsed;
        SearchBox.Text = string.Empty;
        _ = ReloadAsync(periodChanged: false);
    }

    private void FocusSearch()
    {
        CloseSettings();
        SearchBox.Focus(FocusState.Keyboard);
    }

    // ---- Settings --------------------------------------------------------------------------------------------

    private bool SettingsOpen => SettingsHost.Visibility == Visibility.Visible;

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>Shows the Settings page in place of the calendar.</summary>
    public void OpenSettings()
    {
        if (SettingsOpen)
        {
            return;
        }

        var page = new SettingsView(_settings, Accounts, _actions, _services.Updates);
        page.CloseRequested += (_, _) => CloseSettings();
        page.QuitRequested += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        SettingsHost.Children.Add(page);
        SettingsHost.Visibility = Visibility.Visible;
        CalendarContent.Visibility = Visibility.Collapsed;
        page.Loaded += (_, _) => page.Focus(FocusState.Programmatic);
    }

    private void CloseSettings()
    {
        if (!SettingsOpen)
        {
            return;
        }

        SettingsHost.Visibility = Visibility.Collapsed;
        SettingsHost.Children.Clear();
        CalendarContent.Visibility = Visibility.Visible;
        SettingsButton.Focus(FocusState.Programmatic);
    }

    // ---- Keyboard --------------------------------------------------------------------------------------------

    /// <summary>
    /// Ctrl+N new event, Ctrl+F search, Ctrl+, settings, F5 sync, Esc back; and when not typing in a box: T today,
    /// D / W / M / A views, Page Up / Page Down (or ← / →) previous and next.
    /// </summary>
    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = IsDown(VirtualKey.Control);
        bool alt = IsDown(VirtualKey.Menu);
        if (ctrl && !alt)
        {
            e.Handled = e.Key switch
            {
                VirtualKey.N => Do(NewEvent),
                VirtualKey.F => Do(FocusSearch),
                (VirtualKey)188 => Do(OpenSettings), // the comma key
                VirtualKey.R => Do(() => _ = _services.Sync.SyncNowAsync()),
                _ => false,
            };
            return;
        }

        if (alt)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Escape when SettingsOpen:
                e.Handled = Do(CloseSettings);
                return;
            case VirtualKey.Escape when _searchQuery is not null:
                e.Handled = Do(CloseSearch);
                return;
            case VirtualKey.F5:
                e.Handled = Do(() => _ = _services.Sync.SyncNowAsync());
                return;
        }

        if (SettingsOpen || IsTyping() || !Accounts.HasAccounts)
        {
            return;
        }

        e.Handled = e.Key switch
        {
            VirtualKey.T => Do(() => ViewModel.GoToTodayCommand.Execute(null)),
            VirtualKey.D => Do(() => ViewModel.ShowView(CalendarViewKind.Day)),
            VirtualKey.W => Do(() => ViewModel.ShowView(CalendarViewKind.Week)),
            VirtualKey.M => Do(() => ViewModel.ShowView(CalendarViewKind.Month)),
            VirtualKey.A => Do(() => ViewModel.ShowView(CalendarViewKind.Agenda)),
            VirtualKey.PageUp or VirtualKey.Left => Do(() => ViewModel.GoToPreviousCommand.Execute(null)),
            VirtualKey.PageDown or VirtualKey.Right => Do(() => ViewModel.GoToNextCommand.Execute(null)),
            _ => false,
        };

        static bool Do(Action action)
        {
            action();
            return true;
        }
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    /// <summary>Whether the focus is somewhere letters and arrows mean something else: a text box, a list, a picker.</summary>
    private bool IsTyping() => FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox or PasswordBox or RichEditBox or AutoSuggestBox
        or ComboBox or ComboBoxItem or ListViewItem or CalendarView or CalendarViewDayItem or SelectorBarItem or ToggleSwitch or Slider;

    /// <summary>Opens the editor for a new event (Ctrl+N, the tray menu).</summary>
    public void NewEvent() => NewEventButton_Click(this, new RoutedEventArgs());

    /// <summary>Loads the current period from the cache and shows it in the matching view.</summary>
    private async Task ReloadAsync(bool periodChanged)
    {
        if (!periodChanged && _openFlyouts > 0)
        {
            _reloadPending = true;
            return;
        }

        int version = ++_loadVersion;
        var period = ViewModel.Period;
        IReadOnlyList<CalendarItem> items;
        try
        {
            items = await Task.Run(() => _data.LoadAsync(period.GridStart, period.GridEnd));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading {Start}..{End} failed", period.GridStart, period.GridEnd);
            return;
        }

        if (version != _loadVersion)
        {
            return;
        }

        bool hasAccounts = Accounts.HasAccounts;
        bool calendar = hasAccounts && _searchQuery is null;
        TimeGrid.Visibility = Show(calendar && period.Kind is CalendarViewKind.Day or CalendarViewKind.Week);
        MonthGrid.Visibility = Show(calendar && period.Kind == CalendarViewKind.Month);
        AgendaList.Visibility = Show(calendar && period.Kind == CalendarViewKind.Agenda);
        if (_searchQuery is { } query)
        {
            await ShowSearchResultsAsync(query);
        }

        switch (period.Kind)
        {
            case CalendarViewKind.Day:
            case CalendarViewKind.Week:
                var days = Enumerable.Range(0, period.GridEnd.DayNumber - period.GridStart.DayNumber)
                    .Select(i => period.GridStart.AddDays(i)).ToList();
                TimeGrid.Show(days, items, _settings.WorkdayStartHour, _settings.WorkdayEndHour,
                              TimeSpan.FromMinutes(_settings.JoinLeadMinutes), scrollToStart: periodChanged);
                break;
            case CalendarViewKind.Month:
                MonthGrid.Show(period.GridStart, period.GridEnd, period.Start.Month, items);
                break;
            case CalendarViewKind.Agenda:
                AgendaList.Show(period.Start, period.End, items);
                break;
        }

        static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void View_ItemClicked(object? sender, ItemClickedEventArgs e)
    {
        try
        {
            // A flyout created in code needs the window's XamlRoot; ShowAt doesn't infer it in desktop apps.
            var flyout = new Flyout { XamlRoot = e.Anchor.XamlRoot };
            flyout.Content = new EventDetailsView(
                e.Item, e.Anchor.ActualTheme, AppServices.OpenInBrowserAsync,
                async (item, account) =>
                {
                    flyout.Hide();
                    await _join.JoinAsync(item, account);
                },
                _actions,
                flyout.Hide);
            HoldReloadsWhileOpen(flyout);
            flyout.ShowAt(e.Anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Auto });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't show the details of {Event}", e.Item.Event.Key.EventId);
        }
    }

    /// <summary>Empty time was clicked or dragged over: a small flyout to name the event and save it.</summary>
    private async void View_SlotSelected(object? sender, SlotSelectedEventArgs e)
    {
        try
        {
            var calendars = await _actions.GetWritableCalendarsAsync();
            if (calendars.Count == 0)
            {
                TimeGrid.ClearSelection();
                return;
            }

            var flyout = new Flyout { XamlRoot = e.Anchor.XamlRoot };
            flyout.Content = new QuickCreateView(
                e, calendars, _actions.DefaultCalendar(calendars), e.Anchor.ActualTheme,
                async (title, calendar) =>
                {
                    flyout.Hide();
                    var edit = new EventEdit
                    {
                        Summary = title.Length > 0 ? title : null,
                        Start = e.AllDay ? EventTime.AllDay(DateOnly.FromDateTime(e.Start)) : TimeZones.At(e.Start, null),
                        End = e.AllDay ? EventTime.AllDay(DateOnly.FromDateTime(e.End)) : TimeZones.At(e.End, null),
                        IsBusy = e.AllDay ? false : null,
                    };
                    if (!EventActions.IsOnline)
                    {
                        await EditPrompts.ShowErrorAsync(Content.XamlRoot, ((FrameworkElement)Content).ActualTheme, "You're offline",
                                                         "Changes need an internet connection. Your calendar is still here to look at.");
                        return;
                    }

                    await _actions.CreateAsync(calendar, edit);
                },
                async (title, calendar) =>
                {
                    flyout.Hide();
                    await _actions.NewEventAsync(new NewEventDraft(e.Start, e.End, e.AllDay, title, calendar));
                });
            flyout.Closed += (_, _) => TimeGrid.ClearSelection();
            HoldReloadsWhileOpen(flyout);
            flyout.ShowAt(e.Anchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Auto });
        }
        catch (Exception ex)
        {
            TimeGrid.ClearSelection();
            _logger.LogError(ex, "Couldn't start a new event");
        }
    }

    private void HoldReloadsWhileOpen(Flyout flyout)
    {
        // Counted from Opened, so a flyout that fails to show never holds reloads back.
        flyout.Opened += (_, _) => _openFlyouts++;
        flyout.Closed += (_, _) =>
        {
            if (--_openFlyouts == 0 && _reloadPending)
            {
                _reloadPending = false;
                _ = ReloadAsync(periodChanged: false);
            }
        };
    }

    private async void View_ItemMoved(object? sender, ItemMovedEventArgs e)
    {
        try
        {
            if (!await _actions.MoveAsync(e.Item, e.Start, e.End))
            {
                TimeGrid.CancelDrag();
            }
        }
        catch (Exception ex)
        {
            TimeGrid.CancelDrag();
            _logger.LogError(ex, "Couldn't move {Event}", e.Item.Event.Key.EventId);
        }
    }

    private async void NewEventButton_Click(object sender, RoutedEventArgs e)
    {
        // Today's next hour, or 9:00 on the first day shown when the view is elsewhere.
        var period = ViewModel.Period;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var start = today >= period.Start && today < period.End
            ? DateTime.Now.Date.AddHours(DateTime.Now.Hour + 1)
            : period.Start.ToDateTime(new TimeOnly(9, 0));
        try
        {
            await _actions.NewEventAsync(new NewEventDraft(start, start.AddHours(1), false));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't open the event editor");
        }
    }

    private void View_DayClicked(object? sender, DateOnly day) => ViewModel.ShowDay(day);

    private async void View_JoinClicked(object? sender, CalendarItem item) => await _join.JoinAsync(item);

    /// <summary>Follows Windows' light/dark mode unless Settings picks one.</summary>
    private void ApplyTheme()
    {
        if (Content is not FrameworkElement root)
        {
            return;
        }

        root.RequestedTheme = _settings.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        // The caption buttons follow the window's own theme unless the app overrides it.
        Windows.UI.Color? buttons = _settings.Theme switch
        {
            AppTheme.Light => Microsoft.UI.Colors.Black,
            AppTheme.Dark => Microsoft.UI.Colors.White,
            _ => null,
        };
        AppWindow.TitleBar.ButtonForegroundColor = buttons;
        AppWindow.TitleBar.ButtonHoverForegroundColor = buttons;
    }

    private void UpdateControlsFromPeriod()
    {
        var period = ViewModel.Period;
        _updatingControls = true;
        try
        {
            ViewSwitcher.SelectedItem = ViewSwitcher.Items[(int)period.Kind];

            var anchor = new DateTimeOffset(period.Anchor.ToDateTime(TimeOnly.MinValue));
            MiniMonth.SelectedDates.Clear();
            MiniMonth.SelectedDates.Add(anchor);
            MiniMonth.SetDisplayDate(anchor);
        }
        finally
        {
            _updatingControls = false;
        }
    }

    /// <summary>A line above Settings when the pane's content scrolls behind it, as in Windows' navigation panes.</summary>
    private void PaneScroller_SizeChanged(object sender, SizeChangedEventArgs e) =>
        PaneFooter.BorderThickness = PaneScroller.ScrollableHeight > 0 ? new Thickness(0, 1, 0, 0) : new Thickness(0);

    private const double PaneWidth = 312;
    private static readonly Windows.UI.ViewManagement.UISettings SystemSettings = new();
    private bool _paneOpen = true;
    private EventHandler<object>? _paneAnimation;

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args) => SetPaneOpen(!_paneOpen);

    /// <summary>
    /// Opens or closes the side pane. The pane's column grows or shrinks a little on every frame, so the calendar
    /// resizes together with the pane rather than jumping to its final width while the pane is still moving. The
    /// pane keeps its width and is anchored to the right of its column, so it slides in from the window's edge.
    /// With Windows' animation effects off, it opens and closes at once.
    /// </summary>
    private void SetPaneOpen(bool open)
    {
        _paneOpen = open;
        if (_paneAnimation is not null)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= _paneAnimation;
            _paneAnimation = null;
        }

        double from = PaneColumn.Width.Value, to = open ? PaneWidth : 0;
        SidePane.Visibility = Visibility.Visible;
        if (!SystemSettings.AnimationsEnabled || from == to)
        {
            Finish();
            return;
        }

        // Windows' standard motion: a quick ease-out, a little shorter when closing.
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double duration = open ? 250 : 200;
        _paneAnimation = (_, _) =>
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / duration);
            double eased = 1 - Math.Pow(1 - t, 3);
            SetPaneWidth(from + (to - from) * eased);
            if (t >= 1)
            {
                Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= _paneAnimation;
                _paneAnimation = null;
                Finish();
            }
        };
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += _paneAnimation;

        void Finish()
        {
            SetPaneWidth(to);
            SidePane.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Sets how much of the pane shows. The calendar's rounded top-left corner and left edge belong next to the pane,
    /// so they fade out with it and the calendar sits square against the window edge when the pane is closed.
    /// </summary>
    private void SetPaneWidth(double width)
    {
        PaneColumn.Width = new GridLength(width);
        double shown = Math.Clamp(width / PaneWidth, 0, 1);
        ContentArea.CornerRadius = new CornerRadius(8 * shown, 0, 0, 0);
        ContentArea.BorderThickness = new Thickness(width > 0.5 ? 1 : 0, 1, 0, 0);
    }

    // ---- Window icon ----------------------------------------------------------------------------------------

    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _iconClock;
    private int _iconDay;

    /// <summary>
    /// Shows today's date on the window's icon (Alt+Tab and Task View) and on its taskbar button (see
    /// <see cref="TaskbarDate"/>), like the Calendar icon on a Mac. Checked every minute, so it changes at midnight and
    /// after sleep. Notifications show the plain logo.
    /// </summary>
    private async void ShowTodayIcon()
    {
        int day = DateTime.Now.Day;
        string icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Days", $"day-{day}.ico");
        if (day == _iconDay)
        {
            if (AppIdentity.InstalledAppId is not null)
            {
                TaskbarDate.ReadableCopy(icon); // put back if a Temp cleanup removed it
            }

            return;
        }

        bool redraw = _iconDay != 0 && AppWindow.IsVisible;
        try
        {
            AppWindow.SetIcon(icon);
            _iconDay = day;
            await TaskbarDate.ShowAsync(WinRT.Interop.WindowNative.GetWindowHandle(this), icon, redraw);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't show today's icon");
            AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        }
    }

    // ---- Title bar -------------------------------------------------------------------------------------------

    private const double SearchBoxMaxWidth = 440;

    /// <summary>
    /// Centres the search box on the whole window rather than on the space between the pane button and the window
    /// buttons. It stays inside the title bar's content area (so the title bar keeps it clickable instead of making
    /// it part of the drag area), offset by however far that area is from the window's centre.
    /// </summary>
    private void SearchBox_Loaded(object sender, RoutedEventArgs e) => CenterSearchBox();

    private void CenterSearchBox()
    {
        if (VisualTreeHelper.GetParent(SearchBox) is not FrameworkElement host || host.ActualWidth <= 0
            || Content is not FrameworkElement root)
        {
            return;
        }

        const double gap = 4;
        double hostLeft = host.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point(0, 0)).X;
        double width = Math.Max(0, Math.Min(SearchBoxMaxWidth, host.ActualWidth - 2 * gap));
        double left = (root.ActualWidth - width) / 2 - hostLeft;
        left = Math.Clamp(left, gap, Math.Max(gap, host.ActualWidth - width - gap));
        SearchBox.Width = width;
        SearchBox.Margin = new Thickness(left, 0, 0, 0);
    }

    private void MiniMonth_SelectedDatesChanged(CalendarView sender, CalendarViewSelectedDatesChangedEventArgs args)
    {
        if (_updatingControls || args.AddedDates.Count == 0)
        {
            return;
        }

        ViewModel.ShowDate(DateOnly.FromDateTime(args.AddedDates[0].Date));
    }

    private void ViewSwitcher_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_updatingControls || sender.SelectedItem?.Tag is not string tag)
        {
            return;
        }

        ViewModel.ShowView(Enum.Parse<CalendarViewKind>(tag));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}

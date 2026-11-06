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
using Windows.Graphics;
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

    public MainWindow(AppServices services, AppSettings settings, JoinService join)
    {
        _services = services;
        _settings = settings;
        _join = join;
        _data = new CalendarDataService(services);
        _logger = services.Loggers.CreateLogger<MainWindow>();
        _edits = new EventEditService(services);
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
        AppWindow.SetIcon("Assets/app.ico");
        ApplyTheme();
        SizeAndCenter();

        MiniMonth.FirstDayOfWeek = weekStart;
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        UpdateControlsFromPeriod();

        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                _services.Sync.OnWindowActivated();
            }
        };
        services.Sync.Completed += (_, _) => _ = ReloadAsync(periodChanged: false);
        Accounts.CalendarsChanged += (_, _) => _ = ReloadAsync(periodChanged: false);
        _edits.Changed += (_, _) => _ = ReloadAsync(periodChanged: false);
        _actions.WorkingChanged += (_, working) => SavingBar.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        TimeGrid.CanMove = item => item.EditableCopy is not null;

        // Closing the window hides it; Dayflower keeps running in the notification area to remind about events.
        AppWindow.Closing += (_, args) =>
        {
            if (!_closingForGood)
            {
                args.Cancel = true;
                AppWindow.Hide();
                HiddenToTray?.Invoke(this, EventArgs.Empty);
            }
        };
        _ = StartAsync();
    }

    /// <summary>Raised when closing the window hid it to the notification area.</summary>
    public event EventHandler? HiddenToTray;

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
            UpdateControlsFromPeriod();
            _ = ReloadAsync(periodChanged: true);
        }
    }

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
        TimeGrid.Visibility = Show(hasAccounts && period.Kind is CalendarViewKind.Day or CalendarViewKind.Week);
        MonthGrid.Visibility = Show(hasAccounts && period.Kind == CalendarViewKind.Month);
        AgendaList.Visibility = Show(hasAccounts && period.Kind == CalendarViewKind.Agenda);

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

    /// <summary>Follows Windows' light/dark mode unless settings.json picks one.</summary>
    private void ApplyTheme()
    {
        if (_settings.Theme == AppTheme.System || Content is not FrameworkElement root)
        {
            return;
        }

        bool light = _settings.Theme == AppTheme.Light;
        root.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;
        AppWindow.TitleBar.ButtonForegroundColor = light ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
        AppWindow.TitleBar.ButtonHoverForegroundColor = AppWindow.TitleBar.ButtonForegroundColor;
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

    private void AppTitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        SidePane.IsPaneOpen = !SidePane.IsPaneOpen;
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

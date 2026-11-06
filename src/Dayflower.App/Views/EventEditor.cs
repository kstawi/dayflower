// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using Dayflower.Calendar;
using Dayflower.Core.Display;
using Dayflower.Core.Editing;
using Dayflower.Core.Model;
using Dayflower.Core.Providers;
using Dayflower.Core.Recurrence;
using Dayflower.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System.UserProfile;
using Windows.UI;

namespace Dayflower.Views;

/// <summary>A calendar events can be added to, with the account it belongs to.</summary>
public sealed record EditorCalendar(CalendarRecord Calendar, string AccountEmail, Color Color)
{
    public string AccountId => Calendar.AccountId;

    public string Id => Calendar.Id;

    /// <summary>Out of office, focus time and working location exist only on the main calendar of a Workspace account.</summary>
    public bool HasWorkspaceTypes =>
        Calendar.IsPrimary
        && !AccountEmail.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase)
        && !AccountEmail.EndsWith("@googlemail.com", StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Calendar.Name == AccountEmail ? Calendar.Name : $"{Calendar.Name} · {AccountEmail}";
}

/// <summary>Where a new event starts out: its time, and optionally a title and calendar from the quick-create flyout.</summary>
public sealed record NewEventDraft(DateTime Start, DateTime End, bool AllDay, string? Title = null, EditorCalendar? Calendar = null);

/// <summary>What the editor produced: the calendar, and the change (for a new event, everything set).</summary>
public sealed record EditorResult(EditorCalendar Calendar, EventEdit Edit);

/// <summary>
/// The full event editor, in a dialog: title, calendar, type, time and time zone, repeat, location, Google Meet,
/// guests with their permissions and availability, notifications, colour, busy/free, visibility, description, and
/// the settings of out of office, focus time and working location events. For an existing event only what changed
/// is sent.
/// </summary>
public sealed partial class EventEditor
{
    private static readonly string[] ColorNames =
        ["", "Lavender", "Sage", "Grape", "Flamingo", "Banana", "Tangerine", "Peacock", "Graphite", "Blueberry", "Basil", "Tomato"];

    private static readonly (string Text, int Minutes)[] ReminderUnits = [("minutes", 1), ("hours", 60), ("days", 1440), ("weeks", 10080)];

    private readonly XamlRoot _root;
    private readonly ElementTheme _theme;
    private readonly EventEditService _edits;
    private readonly Func<string, string?> _eventColor;
    private readonly IReadOnlyList<EditorCalendar> _calendars;

    // The event being edited (null for a new one), the series it belongs to, and what the form started with.
    private readonly EventOccurrence? _existing;
    private readonly CalendarEvent? _event;
    private readonly IReadOnlyList<string> _initialRecurrence;
    private readonly Snapshot _initial;

    private readonly ContentDialog _dialog;
    private readonly TextBox _title = new() { PlaceholderText = "Add title", FontSize = 20, MaxLength = 1024 };
    private readonly ComboBox _calendar = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly RadioButtons _kind = new() { MaxColumns = 4 };
    private readonly CheckBox _allDay = new() { Content = "All day", MinWidth = 0 };
    private readonly CalendarDatePicker _startDate = DatePicker();
    private readonly CalendarDatePicker _endDate = DatePicker();
    private readonly TimeBox _startTime;
    private readonly TimeBox _endTime;
    private readonly ComboBox _zone = new() { HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
    private readonly HyperlinkButton _zoneLink = new() { Content = "Time zone", Padding = new Thickness(4, 0, 4, 0) };
    private readonly TextBlock _timeError = new() { Visibility = Visibility.Collapsed, Text = "The event ends before it starts." };
    private readonly ComboBox _repeat = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly StackPanel _customRepeat = new() { Spacing = 8, Visibility = Visibility.Collapsed };
    private readonly NumberBox _repeatEvery = new() { Minimum = 1, Maximum = 99, Value = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 110 };
    private readonly ComboBox _repeatUnit = new() { Width = 120 };
    private readonly StackPanel _repeatDays = new() { Orientation = Orientation.Horizontal, Spacing = 4 };
    private readonly ComboBox _repeatMonthly = new() { MinWidth = 220 };
    private readonly RadioButtons _repeatEnds = new() { Header = "Ends" };
    private readonly CalendarDatePicker _repeatUntil = DatePicker();
    private readonly NumberBox _repeatCount = new() { Minimum = 1, Maximum = 730, Value = 10, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, Width = 110 };
    private readonly TextBox _location = new() { PlaceholderText = "Add location", MaxLength = 1024 };
    private readonly ToggleButton _meet = new();
    private readonly AutoSuggestBox _guestBox = new() { PlaceholderText = "Add guests", QueryIcon = new SymbolIcon(Symbol.Add) };
    private readonly StackPanel _guestList = new() { Spacing = 2 };
    private readonly CheckBox _canModify = new() { Content = "Modify event", MinWidth = 0 };
    private readonly CheckBox _canInvite = new() { Content = "Invite others", MinWidth = 0 };
    private readonly CheckBox _canSeeGuests = new() { Content = "See guest list", MinWidth = 0 };
    private readonly StackPanel _availability = new() { Spacing = 4, Visibility = Visibility.Collapsed };
    private readonly HyperlinkButton _availabilityLink = new() { Content = "Check availability", Padding = new Thickness(0) };
    private readonly StackPanel _reminderList = new() { Spacing = 6 };
    private readonly ComboBox _color = new() { MinWidth = 200 };
    private readonly ComboBox _busy = new() { MinWidth = 140 };
    private readonly ComboBox _visibility = new() { MinWidth = 180 };
    private readonly TextBox _description = new() { PlaceholderText = "Add description", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 96, MaxHeight = 240 };
    private readonly ComboBox _autoDecline = new() { Header = "Automatically decline meetings", MinWidth = 280 };
    private readonly TextBox _declineMessage = new() { Header = "Message", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, MaxLength = 1000 };
    private readonly RadioButtons _workingLocation = new() { Header = "Working from", MaxColumns = 3 };
    private readonly TextBox _workingLabel = new() { PlaceholderText = "Building, office or place" };

    private readonly List<Guest> _guests = [];
    private readonly List<ReminderRow> _reminders = [];
    private IReadOnlyList<KnownPerson> _people = [];
    private CancellationTokenSource? _availabilityLoad;
    private bool _hasMeet;
    private bool _updating;

    private UIElement _guestsSection = null!;
    private UIElement _meetRow = null!;
    private UIElement _locationRow = null!;
    private UIElement _descriptionRow = null!;
    private UIElement _declineSection = null!;
    private UIElement _workingSection = null!;
    private UIElement _repeatRow = null!;
    private UIElement _detailsRows = null!;

    /// <param name="calendars">Calendars the user can add events to (for a new event).</param>
    /// <param name="existing">The occurrence to edit, or null for a new event.</param>
    /// <param name="existingCalendar">The calendar of <paramref name="existing"/>.</param>
    /// <param name="seriesRecurrence">For a changed occurrence of a series, the series' repeat rule.</param>
    public EventEditor(
        XamlRoot root, ElementTheme theme, EventEditService edits, Func<string, string?> eventColor,
        IReadOnlyList<EditorCalendar> calendars, EventOccurrence? existing, EditorCalendar? existingCalendar,
        NewEventDraft? draft, IReadOnlyList<string>? seriesRecurrence = null)
    {
        _root = root;
        _theme = theme;
        _edits = edits;
        _eventColor = eventColor;
        _existing = existing;
        _event = existing?.Event;
        _calendars = existing is null ? calendars : [existingCalendar!];
        _initialRecurrence = seriesRecurrence ?? _event?.Recurrence ?? [];

        var now = DateTime.Now;
        var start = draft?.Start ?? now.Date.AddHours(now.Hour + 1);
        _startTime = new TimeBox(TimeOnly.FromDateTime(start));
        _endTime = new TimeBox(TimeOnly.FromDateTime(draft?.End ?? start.AddHours(1)));
        _timeError.Foreground = Brush("SystemFillColorCriticalBrush");

        BuildCalendarChoice(draft?.Calendar ?? existingCalendar);
        Load(existing, draft);
        _initial = Capture();

        _dialog = new ContentDialog
        {
            Title = existing is null ? "New event" : "Edit event",
            Content = BuildForm(),
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root,
            RequestedTheme = theme,
            Style = EventVisuals.Resource<Style>("DefaultContentDialogStyle"),
        };
        _dialog.Resources["ContentDialogMaxWidth"] = 720.0;
        _dialog.Resources["ContentDialogMinWidth"] = 640.0;
        _dialog.Resources["ContentDialogMaxHeight"] = 1200.0;
        _dialog.Opened += async (_, _) =>
        {
            _title.Focus(FocusState.Programmatic);
            _title.SelectAll();
            _people = await _edits.GetKnownPeopleAsync();
        };
        NameControls();
        UpdateVisibility();
        Validate();
    }

    private EditorCalendar Calendar => _calendar.SelectedItem as EditorCalendar ?? _calendars[0];

    private EventKind Kind => _event?.Kind ?? (_kind.Visibility != Visibility.Visible ? EventKind.Default : _kind.SelectedIndex switch
    {
        1 => EventKind.OutOfOffice,
        2 => EventKind.FocusTime,
        3 => EventKind.WorkingLocation,
        _ => EventKind.Default,
    });

    /// <summary>Shows the editor; returns what to save, or null when cancelled.</summary>
    public async Task<EditorResult?> ShowAsync()
    {
        while (await _dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (!Validate())
            {
                continue;
            }

            return new EditorResult(Calendar, _existing is null ? NewEdit() : ChangedEdit());
        }

        return null;
    }

    // ---- Loading ---------------------------------------------------------------------------------------------

    private void BuildCalendarChoice(EditorCalendar? selected)
    {
        foreach (var calendar in _calendars)
        {
            _calendar.Items.Add(calendar);
        }

        _calendar.SelectedItem = _calendars.FirstOrDefault(c => selected is not null && c.AccountId == selected.AccountId && c.Id == selected.Id)
                                 ?? _calendars[0];
        _calendar.IsEnabled = _existing is null && _calendars.Count > 1;
        _calendar.SelectionChanged += (_, _) =>
        {
            if (_updating)
            {
                return;
            }

            UpdateKindChoice();
            if (!_remindersTouched)
            {
                SetReminders(Calendar.Calendar.DefaultReminders);
            }

            UpdateVisibility();
        };

        _kind.Items.Add("Event");
        _kind.Items.Add("Out of office");
        _kind.Items.Add("Focus time");
        _kind.Items.Add("Working location");
        _kind.SelectedIndex = 0;
        _kind.SelectionChanged += (_, _) =>
        {
            if (Kind == EventKind.WorkingLocation)
            {
                _allDay.IsChecked = true;
            }

            UpdateVisibility();
            Validate();
        };
        UpdateKindChoice();
    }

    private void UpdateKindChoice()
    {
        bool show = _existing is null && Calendar.HasWorkspaceTypes;
        _kind.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show)
        {
            _kind.SelectedIndex = 0;
        }
    }

    private bool _remindersTouched;

    private void Load(EventOccurrence? existing, NewEventDraft? draft)
    {
        _updating = true;
        var e = existing?.Event;
        _title.Text = e?.Summary ?? draft?.Title ?? string.Empty;

        if (existing is not null)
        {
            bool allDay = existing.Start.IsAllDay;
            _allDay.IsChecked = allDay;
            if (allDay)
            {
                SetDate(_startDate, existing.Start.Date!.Value);
                SetDate(_endDate, existing.End.Date!.Value.AddDays(-1) < existing.Start.Date!.Value ? existing.Start.Date!.Value : existing.End.Date!.Value.AddDays(-1));
                _startTime.Set(new TimeOnly(9, 0));
                _endTime.Set(new TimeOnly(10, 0));
            }
            else
            {
                var start = TimeZones.WallTime(existing.Start.DateTime!.Value, null);
                var end = TimeZones.WallTime(existing.End.DateTime!.Value, null);
                SetDate(_startDate, DateOnly.FromDateTime(start));
                SetDate(_endDate, DateOnly.FromDateTime(end));
                _startTime.Set(TimeOnly.FromDateTime(start));
                _endTime.Set(TimeOnly.FromDateTime(end));
            }
        }
        else
        {
            var start = draft?.Start ?? DateTime.Now.Date.AddHours(DateTime.Now.Hour + 1);
            var end = draft?.End ?? start.AddHours(1);
            _allDay.IsChecked = draft?.AllDay == true;
            SetDate(_startDate, DateOnly.FromDateTime(start));
            SetDate(_endDate, draft?.AllDay == true ? DateOnly.FromDateTime(end).AddDays(-1) : DateOnly.FromDateTime(end));
            if (draft?.AllDay == true)
            {
                _startTime.Set(new TimeOnly(9, 0));
                _endTime.Set(new TimeOnly(10, 0));
            }
        }

        foreach (var (id, name) in TimeZones.All)
        {
            _zone.Items.Add(new ComboBoxItem { Content = name, Tag = id });
        }

        _zone.SelectedItem = _zone.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == TimeZones.LocalId);

        _location.Text = e?.Location ?? string.Empty;
        _hasMeet = e is not null && (e.HangoutLink is not null || e.Conference?.SolutionName is not null);
        _meet.IsChecked = _hasMeet;
        UpdateMeetText();

        foreach (var a in e?.Attendees.Where(a => !a.IsSelf && !a.IsResource) ?? [])
        {
            _guests.Add(new Guest(a.Email ?? string.Empty, a.DisplayName, a.IsOptional, a.Response, a.IsOrganizer));
        }

        _canModify.IsChecked = e?.GuestsCanModify ?? false;
        _canInvite.IsChecked = e?.GuestsCanInviteOthers ?? true;
        _canSeeGuests.IsChecked = e?.GuestsCanSeeOtherGuests ?? true;

        if (e is null || e.Reminders.UseDefault)
        {
            SetReminders(Calendar.Calendar.DefaultReminders);
        }
        else
        {
            SetReminders(e.Reminders.Overrides);
        }

        _color.Items.Add(ColorItem(Calendar.Color, "Calendar colour", string.Empty));
        for (int i = 1; i < ColorNames.Length; i++)
        {
            string id = i.ToString(CultureInfo.InvariantCulture);
            if (_eventColor(id) is { } hex)
            {
                _color.Items.Add(ColorItem(CalendarItemViewModelColor(hex), ColorNames[i], id));
            }
        }

        _color.SelectedItem = _color.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (e?.ColorId ?? string.Empty)) ?? _color.Items[0];

        _busy.Items.Add("Busy");
        _busy.Items.Add("Free");
        _busy.SelectedIndex = e is null ? (draft?.AllDay == true ? 1 : 0) : e.IsBusy ? 0 : 1;

        _visibility.Items.Add("Default visibility");
        _visibility.Items.Add("Public");
        _visibility.Items.Add("Private");
        _visibility.SelectedIndex = e?.Visibility switch
        {
            EventVisibility.Public => 1,
            EventVisibility.Private or EventVisibility.Confidential => 2,
            _ => 0,
        };

        _description.Text = HtmlText.ToPlainText(e?.Description);

        _autoDecline.Items.Add("Don't decline");
        _autoDecline.Items.Add("Only new invitations");
        _autoDecline.Items.Add("New and existing meetings");
        _autoDecline.SelectedIndex = e?.Workspace?.AutoDecline switch
        {
            AutoDeclineMode.AllConflicting => 2,
            AutoDeclineMode.None => 0,
            _ => e is null ? 1 : 0,
        };
        _declineMessage.Text = e?.Workspace?.DeclineMessage ?? string.Empty;

        _workingLocation.Items.Add("Home");
        _workingLocation.Items.Add("Office");
        _workingLocation.Items.Add("Somewhere else");
        _workingLocation.SelectedIndex = e?.Workspace?.Location switch
        {
            WorkingLocationType.Office => 1,
            WorkingLocationType.Custom => 2,
            _ => 0,
        };
        _workingLabel.Text = e?.Workspace?.LocationLabel ?? string.Empty;

        LoadRepeat();
        _updating = false;
        RefreshGuests();
    }

    private static Color CalendarItemViewModelColor(string hex) => ViewModels.CalendarItemViewModel.ParseColor(hex);

    private static ComboBoxItem ColorItem(Color color, string name, string id)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = name });
        return new ComboBoxItem { Content = content, Tag = id };
    }

    // ---- Layout ----------------------------------------------------------------------------------------------

    private StackPanel BuildForm()
    {
        var form = new StackPanel { Spacing = 14, Padding = new Thickness(0, 0, 12, 0) };
        form.Children.Add(_title);
        form.Children.Add(_kind);
        form.Children.Add(Row("", _calendar));

        // When: dates, times, all day and time zone.
        var times = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        times.Children.Add(_startDate);
        times.Children.Add(_startTime.Box);
        times.Children.Add(new TextBlock { Text = "–", VerticalAlignment = VerticalAlignment.Center });
        times.Children.Add(_endTime.Box);
        times.Children.Add(_endDate);
        var whenOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        whenOptions.Children.Add(_allDay);
        whenOptions.Children.Add(_zoneLink);
        var when = new StackPanel { Spacing = 6 };
        when.Children.Add(times);
        when.Children.Add(whenOptions);
        when.Children.Add(_zone);
        when.Children.Add(_timeError);
        form.Children.Add(Row("", when));

        _startDate.DateChanged += (_, _) => OnStartChanged();
        _startTime.Changed += OnStartChanged;
        _endDate.DateChanged += (_, _) => OnTimesChanged();
        _endTime.Changed += OnTimesChanged;
        _allDay.Checked += (_, _) => OnAllDayChanged();
        _allDay.Unchecked += (_, _) => OnAllDayChanged();
        _zoneLink.Click += (_, _) =>
        {
            _zone.Visibility = Visibility.Visible;
            _zoneLink.Visibility = Visibility.Collapsed;
        };

        // Repeat.
        var repeat = new StackPanel { Spacing = 8 };
        repeat.Children.Add(_repeat);
        repeat.Children.Add(BuildCustomRepeat());
        _repeatRow = Row("", repeat);
        form.Children.Add(_repeatRow);

        // Workspace event settings.
        var decline = new StackPanel { Spacing = 8 };
        decline.Children.Add(_autoDecline);
        decline.Children.Add(_declineMessage);
        _declineSection = Row("", decline);
        form.Children.Add(_declineSection);
        var working = new StackPanel { Spacing = 8 };
        working.Children.Add(_workingLocation);
        working.Children.Add(_workingLabel);
        _workingLocation.SelectionChanged += (_, _) => UpdateVisibility();
        _workingSection = Row("", working);
        form.Children.Add(_workingSection);

        _locationRow = Row("", _location);
        form.Children.Add(_locationRow);

        _meet.Click += (_, _) => UpdateMeetText();
        _meetRow = Row("", _meet);
        form.Children.Add(_meetRow);

        // Guests.
        var guests = new StackPanel { Spacing = 8 };
        guests.Children.Add(_guestBox);
        guests.Children.Add(_guestList);
        var permissions = new StackPanel { Spacing = 0 };
        permissions.Children.Add(new TextBlock { Text = "Guests can", Style = EventVisuals.Resource<Style>("CaptionTextBlockStyle"), Foreground = Brush("TextFillColorSecondaryBrush") });
        var boxes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        boxes.Children.Add(_canModify);
        boxes.Children.Add(_canInvite);
        boxes.Children.Add(_canSeeGuests);
        permissions.Children.Add(boxes);
        guests.Children.Add(permissions);
        guests.Children.Add(_availabilityLink);
        guests.Children.Add(_availability);
        _guestsSection = Row("", guests);
        form.Children.Add(_guestsSection);
        _guestBox.TextChanged += GuestBox_TextChanged;
        _guestBox.QuerySubmitted += (_, args) =>
        {
            // Enter on a typed name picks the first suggestion; otherwise the text is taken as addresses.
            var person = (args.ChosenSuggestion as PersonSuggestion)?.Person;
            if (person is null && !args.QueryText.Contains('@') && _guestBox.ItemsSource is IReadOnlyList<PersonSuggestion> { Count: > 0 } suggestions)
            {
                person = suggestions[0].Person;
            }

            AddGuest(person?.Email ?? args.QueryText, person?.Name);
        };
        _availabilityLink.Click += async (_, _) =>
        {
            _availability.Visibility = Visibility.Visible;
            _availabilityLink.Visibility = Visibility.Collapsed;
            await LoadAvailabilityAsync();
        };

        // Notifications.
        var reminders = new StackPanel { Spacing = 6 };
        reminders.Children.Add(_reminderList);
        var addReminder = new HyperlinkButton { Content = "Add notification", Padding = new Thickness(0) };
        addReminder.Click += (_, _) =>
        {
            _remindersTouched = true;
            AddReminderRow(new Reminder(ReminderMethod.Popup, 30));
        };
        reminders.Children.Add(addReminder);

        // Colour, busy/free, visibility.
        var looks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        looks.Children.Add(_color);
        looks.Children.Add(_busy);
        looks.Children.Add(_visibility);

        var details = new StackPanel { Spacing = 14 };
        details.Children.Add(Row("", reminders));
        details.Children.Add(Row("", looks));
        _descriptionRow = Row("", _description);
        details.Children.Add(_descriptionRow);
        _detailsRows = details;
        form.Children.Add(details);
        return form;
    }

    private StackPanel BuildCustomRepeat()
    {
        _repeatUnit.Items.Add("days");
        _repeatUnit.Items.Add("weeks");
        _repeatUnit.Items.Add("months");
        _repeatUnit.Items.Add("years");
        _repeatUnit.SelectedIndex = 1;
        _repeatUnit.SelectionChanged += (_, _) => UpdateCustomRepeat();

        var every = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        every.Children.Add(new TextBlock { Text = "Every", VerticalAlignment = VerticalAlignment.Center });
        every.Children.Add(_repeatEvery);
        every.Children.Add(_repeatUnit);
        _customRepeat.Children.Add(every);

        var names = CultureInfo.CurrentUICulture.DateTimeFormat;
        var first = (int)GlobalizationPreferences.WeekStartsOn;
        for (int i = 0; i < 7; i++)
        {
            var day = (DayOfWeek)((first + i) % 7);
            var toggle = new ToggleButton
            {
                Content = names.GetShortestDayName(day),
                Tag = day,
                MinWidth = 40,
                Padding = new Thickness(6, 4, 6, 4),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, names.GetDayName(day));
            _repeatDays.Children.Add(toggle);
        }

        _customRepeat.Children.Add(_repeatDays);
        _customRepeat.Children.Add(_repeatMonthly);

        _repeatEnds.Items.Add("Never");
        _repeatEnds.Items.Add("On");
        _repeatEnds.Items.Add("After");
        _repeatEnds.SelectedIndex = 0;
        _repeatEnds.SelectionChanged += (_, _) => UpdateCustomRepeat();
        var ends = new Grid { ColumnSpacing = 12 };
        ends.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ends.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ends.Children.Add(_repeatEnds);
        var endValues = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Bottom };
        endValues.Children.Add(_repeatUntil);
        var countRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        countRow.Children.Add(_repeatCount);
        countRow.Children.Add(new TextBlock { Text = "times", VerticalAlignment = VerticalAlignment.Center });
        endValues.Children.Add(countRow);
        Grid.SetColumn(endValues, 1);
        ends.Children.Add(endValues);
        _customRepeat.Children.Add(ends);
        return _customRepeat;
    }

    private Grid Row(string glyph, UIElement content)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            Foreground = Brush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 0, 0),
        });
        Grid.SetColumn((FrameworkElement)content, 1);
        row.Children.Add(content);
        return row;
    }

    private void UpdateVisibility()
    {
        var kind = Kind;
        bool normal = kind == EventKind.Default;
        bool allDay = _allDay.IsChecked == true;
        Show(_guestsSection, normal);
        Show(_meetRow, normal);
        Show(_locationRow, normal);
        Show(_descriptionRow, normal);
        Show(_declineSection, kind is EventKind.OutOfOffice or EventKind.FocusTime);
        Show(_workingSection, kind == EventKind.WorkingLocation);
        Show(_workingLabel, kind == EventKind.WorkingLocation && _workingLocation.SelectedIndex > 0);
        Show(_detailsRows, kind is EventKind.Default or EventKind.FocusTime);
        Show(_allDay, kind is EventKind.Default or EventKind.WorkingLocation or EventKind.OutOfOffice);
        Show(_startTime.Box, !allDay);
        Show(_endTime.Box, !allDay);
        Show(_zoneLink, !allDay && _zone.Visibility == Visibility.Collapsed);
        if (allDay)
        {
            _zone.Visibility = Visibility.Collapsed;
        }

        // A changed occurrence can't start repeating by itself; out of office and focus time can repeat, as in Google.
        Show(_repeatRow, _event is null || _event.IsSeries || _initialRecurrence.Count > 0 || !_event.IsOccurrenceOverride);

        static void Show(UIElement element, bool visible) => element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- Times -----------------------------------------------------------------------------------------------

    private DateOnly StartDate => DateOnly.FromDateTime((_startDate.Date ?? DateTimeOffset.Now).Date);

    private DateOnly EndDate => DateOnly.FromDateTime((_endDate.Date ?? DateTimeOffset.Now).Date);

    private DateTime StartWall => StartDate.ToDateTime(_startTime.Value);

    private DateTime EndWall => EndDate.ToDateTime(_endTime.Value);

    private string? PickedZone => _zone.Visibility == Visibility.Visible && _zone.SelectedItem is ComboBoxItem { Tag: string id } ? id : null;

    /// <summary>The time zone written with the event: one picked here, else the event's own, the calendar's or Windows'.</summary>
    private string EventZone => PickedZone ?? _event?.Start.TimeZone ?? Calendar.Calendar.TimeZone ?? TimeZones.LocalId;

    private (EventTime Start, EventTime End) Times()
    {
        if (_allDay.IsChecked == true)
        {
            return (EventTime.AllDay(StartDate), EventTime.AllDay(EndDate.AddDays(1)));
        }

        // Times are entered in Windows' zone unless a zone is picked; the event keeps its own zone either way.
        string? wallZone = PickedZone;
        var start = TimeZones.At(StartWall, wallZone);
        var end = TimeZones.At(EndWall, wallZone);
        return (EventTime.At(start.DateTime!.Value, EventZone), EventTime.At(end.DateTime!.Value, EventZone));
    }

    private TimeSpan _length = TimeSpan.FromHours(1);

    private void OnStartChanged()
    {
        if (_updating)
        {
            return;
        }

        // Moving the start keeps the length, as in Google Calendar.
        _updating = true;
        if (_allDay.IsChecked == true)
        {
            int days = Math.Max(0, (int)_length.TotalDays);
            SetDate(_endDate, StartDate.AddDays(days));
        }
        else
        {
            var end = StartWall + _length;
            SetDate(_endDate, DateOnly.FromDateTime(end));
            _endTime.Set(TimeOnly.FromDateTime(end));
        }

        _updating = false;
        LoadRepeat(keepSelection: true);
        Validate();
        _ = LoadAvailabilityAsync();
    }

    private void OnTimesChanged()
    {
        if (_updating)
        {
            return;
        }

        if (Validate())
        {
            _length = _allDay.IsChecked == true ? TimeSpan.FromDays(EndDate.DayNumber - StartDate.DayNumber) : EndWall - StartWall;
        }

        _ = LoadAvailabilityAsync();
    }

    private void OnAllDayChanged()
    {
        if (_updating)
        {
            return;
        }

        _length = _allDay.IsChecked == true ? TimeSpan.FromDays(Math.Max(0, EndDate.DayNumber - StartDate.DayNumber)) : EndWall - StartWall;
        if (_allDay.IsChecked != true && _length <= TimeSpan.Zero)
        {
            _length = TimeSpan.FromHours(1);
            _updating = true;
            _endTime.Set(TimeOnly.FromDateTime(StartWall + _length));
            _updating = false;
        }

        if (_existing is null)
        {
            // Google shows new all-day events as free, and timed ones as busy.
            _busy.SelectedIndex = _allDay.IsChecked == true ? 1 : 0;
        }

        UpdateVisibility();
        Validate();
    }

    private bool Validate()
    {
        bool valid = _allDay.IsChecked == true ? EndDate >= StartDate : EndWall > StartWall;
        _timeError.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
        if (_dialog is not null)
        {
            _dialog.IsPrimaryButtonEnabled = valid;
        }

        return valid;
    }

    // Written out ("Fri 6 Nov 2026"), so the order of day and month is never ambiguous.
    private static CalendarDatePicker DatePicker() => new()
    {
        FirstDayOfWeek = GlobalizationPreferences.WeekStartsOn,
        IsTodayHighlighted = true,
        MinWidth = 150,
        DateFormat = TimeText.DayBeforeMonth
            ? "{dayofweek.abbreviated} {day.integer} {month.abbreviated} {year.full}"
            : "{dayofweek.abbreviated}, {month.abbreviated} {day.integer}, {year.full}",
    };

    /// <summary>Names for screen readers (and UI tests) on controls that have no visible label.</summary>
    private void NameControls()
    {
        (FrameworkElement Control, string Name)[] names =
        [
            (_title, "Title"), (_calendar, "Calendar"), (_kind, "Event type"), (_startDate, "Start date"), (_startTime.Box, "Start time"),
            (_endTime.Box, "End time"), (_endDate, "End date"), (_zone, "Time zone"), (_repeat, "Repeat"), (_repeatEvery, "Repeat every"),
            (_repeatUnit, "Repeat unit"), (_repeatMonthly, "Repeat monthly on"), (_repeatUntil, "Repeat until"), (_repeatCount, "Number of times"),
            (_location, "Location"), (_meet, "Google Meet"), (_guestBox, "Add guests"), (_color, "Colour"), (_busy, "Show as"),
            (_visibility, "Visibility"), (_description, "Description"), (_workingLabel, "Working location name"),
        ];
        foreach (var (control, name) in names)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, name);
        }
    }

    private static void SetDate(CalendarDatePicker picker, DateOnly date) =>
        picker.Date = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue));

    // ---- Repeat ----------------------------------------------------------------------------------------------

    private RecurrenceRule? _initialRule;
    private string? _unknownRule;

    /// <summary>One choice in the repeat list: a ready-made rule, the event's own rule, one kept as is, or custom.</summary>
    private sealed record RepeatChoice(string Key, RecurrenceRule? Rule);

    /// <summary>The ready-made choices: daily, weekly on this day, monthly on this weekday, yearly, weekdays.</summary>
    private List<(string Key, string Text, RecurrenceRule? Rule)> Presets()
    {
        var day = StartDate;
        var names = CultureInfo.CurrentUICulture;
        int week = (day.Day - 1) / 7 + 1;
        bool isLast = day.AddDays(7).Month != day.Month;
        string dayName = names.DateTimeFormat.GetDayName(day.DayOfWeek);
        var presets = new List<(string, string, RecurrenceRule?)>
        {
            ("none", "Does not repeat", null),
            ("daily", "Daily", new RecurrenceRule { Frequency = RepeatFrequency.Daily }),
            ("weekly", $"Weekly on {dayName}", new RecurrenceRule { Frequency = RepeatFrequency.Weekly, Days = [day.DayOfWeek] }),
            ("monthly", $"Monthly on the {(week > 4 ? "last" : Ordinal(week))} {dayName}",
                new RecurrenceRule { Frequency = RepeatFrequency.Monthly, WeekOfMonth = week > 4 ? -1 : week }),
        };
        if (isLast && week == 4)
        {
            presets.Add(("monthly-last", $"Monthly on the last {dayName}", new RecurrenceRule { Frequency = RepeatFrequency.Monthly, WeekOfMonth = -1 }));
        }

        presets.Add(("yearly", $"Annually on {day.ToString(TimeText.DayBeforeMonth ? "d MMMM" : "MMMM d", names)}", new RecurrenceRule { Frequency = RepeatFrequency.Yearly }));
        presets.Add(("weekdays", "Every weekday (Monday to Friday)", new RecurrenceRule
        {
            Frequency = RepeatFrequency.Weekly,
            Days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        }));
        return presets;
    }

    private static string Ordinal(int n) => n switch { 1 => "first", 2 => "second", 3 => "third", _ => "fourth" };

    private RepeatChoice? SelectedRepeat => (_repeat.SelectedItem as ComboBoxItem)?.Tag as RepeatChoice;

    /// <summary>Fills the repeat list for the current start date, keeping the choice when the date changes.</summary>
    private void LoadRepeat(bool keepSelection = false)
    {
        var previous = keepSelection ? SelectedRepeat : null;
        if (!keepSelection)
        {
            string? line = _initialRecurrence.FirstOrDefault(l => l.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase));
            var startForRule = _existing?.Start ?? EventTime.AllDay(StartDate);
            _initialRule = line is null ? null : RecurrenceRule.Parse(line, startForRule);
            _unknownRule = line is not null && _initialRule is null ? line : null;
        }

        bool wasUpdating = _updating;
        _updating = true;
        _repeat.Items.Clear();
        ComboBoxItem? select = null;
        ComboBoxItem Add(string key, string text, RecurrenceRule? rule)
        {
            var item = new ComboBoxItem { Content = text, Tag = new RepeatChoice(key, rule) };
            _repeat.Items.Add(item);
            return item;
        }

        foreach (var (key, text, rule) in Presets())
        {
            var item = Add(key, text, rule);
            if (previous is not null ? previous.Key == key : Same(rule, _initialRule))
            {
                select ??= item;
            }
        }

        if (_unknownRule is not null)
        {
            var kept = Add("kept", "Custom rule (kept as it is)", null);
            if (previous is null || previous.Key == "kept")
            {
                select = kept;
            }
        }
        else if (select is null && (previous is null ? _initialRule : previous.Key == "rule" ? previous.Rule : null) is { } own)
        {
            select = Add("rule", own.Describe(EventTime.AllDay(StartDate), CultureInfo.CurrentUICulture), own);
        }

        var custom = Add("custom", "Custom…", null);
        if (previous?.Key == "custom")
        {
            select = custom;
        }

        _repeat.SelectedItem = select ?? _repeat.Items[0];
        if (!keepSelection)
        {
            _repeat.SelectionChanged += (_, _) =>
            {
                if (_updating)
                {
                    return;
                }

                if (SelectedRepeat?.Key == "custom")
                {
                    FillCustomRepeat(_lastRule ?? new RecurrenceRule { Frequency = RepeatFrequency.Weekly, Days = [StartDate.DayOfWeek] });
                }

                UpdateCustomRepeat();
            };
        }

        _updating = wasUpdating;
        UpdateCustomRepeat();
    }

    // The last rule picked before "Custom…", to start the custom form from.
    private RecurrenceRule? _lastRule;

    private static bool Same(RecurrenceRule? a, RecurrenceRule? b) =>
        a is null ? b is null : b is not null && a.Frequency == b.Frequency && a.Interval == b.Interval && a.WeekOfMonth == b.WeekOfMonth
            && a.Count == b.Count && a.Until == b.Until && a.Days.Order().SequenceEqual(b.Days.Order());

    private void FillCustomRepeat(RecurrenceRule rule)
    {
        _repeatEvery.Value = rule.Interval;
        _repeatUnit.SelectedIndex = (int)rule.Frequency;
        var days = rule.Days.Count > 0 ? rule.Days : [StartDate.DayOfWeek];
        foreach (var toggle in _repeatDays.Children.OfType<ToggleButton>())
        {
            toggle.IsChecked = days.Contains((DayOfWeek)toggle.Tag);
        }

        _repeatEnds.SelectedIndex = rule.Count is not null ? 2 : rule.Until is not null ? 1 : 0;
        if (rule.Until is { } until)
        {
            SetDate(_repeatUntil, until);
        }
        else
        {
            SetDate(_repeatUntil, StartDate.AddMonths(3));
        }

        _repeatCount.Value = rule.Count ?? 10;
        UpdateCustomRepeat();
        _repeatMonthly.SelectedIndex = rule.WeekOfMonth is null ? 0 : rule.WeekOfMonth == -1 && _repeatMonthly.Items.Count > 2 ? 2 : 1;
    }

    private void UpdateCustomRepeat()
    {
        if (SelectedRepeat?.Rule is { } picked)
        {
            _lastRule = picked;
        }

        bool custom = SelectedRepeat?.Key == "custom";
        _customRepeat.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (!custom)
        {
            return;
        }

        var day = StartDate;
        int week = (day.Day - 1) / 7 + 1;
        string dayName = CultureInfo.CurrentUICulture.DateTimeFormat.GetDayName(day.DayOfWeek);
        int selected = Math.Max(0, _repeatMonthly.SelectedIndex);
        _repeatMonthly.Items.Clear();
        _repeatMonthly.Items.Add($"Monthly on day {day.Day}");
        _repeatMonthly.Items.Add($"Monthly on the {(week > 4 ? "last" : Ordinal(week))} {dayName}");
        if (week == 4 && day.AddDays(7).Month != day.Month)
        {
            _repeatMonthly.Items.Add($"Monthly on the last {dayName}");
        }

        _repeatMonthly.SelectedIndex = Math.Min(selected, _repeatMonthly.Items.Count - 1);
        _repeatDays.Visibility = _repeatUnit.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        _repeatMonthly.Visibility = _repeatUnit.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        _repeatUntil.IsEnabled = _repeatEnds.SelectedIndex == 1;
        _repeatCount.IsEnabled = _repeatEnds.SelectedIndex == 2;
    }

    /// <summary>The repeat rule the form shows: null for none, or the RRULE line and the rest kept from before.</summary>
    private IReadOnlyList<string> Recurrence(EventTime start)
    {
        var kept = _initialRecurrence.Where(l => !l.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase)).ToList();
        switch (SelectedRepeat)
        {
            case null or { Key: "none" }:
                return [];
            case { Key: "kept" }:
                return _initialRecurrence;
            case { Rule: { } rule }:
                return [rule.ToRRule(start), .. kept];
            case { Key: "custom" }:
                var frequency = (RepeatFrequency)Math.Max(0, _repeatUnit.SelectedIndex);
                var day = StartDate;
                int week = (day.Day - 1) / 7 + 1;
                var custom = new RecurrenceRule
                {
                    Frequency = frequency,
                    Interval = double.IsNaN(_repeatEvery.Value) ? 1 : (int)_repeatEvery.Value,
                    Days = frequency == RepeatFrequency.Weekly
                        ? _repeatDays.Children.OfType<ToggleButton>().Where(t => t.IsChecked == true).Select(t => (DayOfWeek)t.Tag).ToList()
                        : [],
                    WeekOfMonth = frequency == RepeatFrequency.Monthly && _repeatMonthly.SelectedIndex > 0
                        ? (_repeatMonthly.SelectedIndex == 2 || week > 4 ? -1 : week)
                        : null,
                    Count = _repeatEnds.SelectedIndex == 2 && !double.IsNaN(_repeatCount.Value) ? (int)_repeatCount.Value : null,
                    Until = _repeatEnds.SelectedIndex == 1 && _repeatUntil.Date is { } until ? DateOnly.FromDateTime(until.Date) : null,
                };
                return [custom.ToRRule(start), .. kept];
            default:
                return [];
        }
    }

    // ---- Guests ----------------------------------------------------------------------------------------------

    private sealed class Guest(string email, string? name, bool isOptional, AttendeeResponse? response, bool isOrganizer = false)
    {
        public string Email { get; } = email;

        public string? Name { get; } = name;

        public bool IsOptional { get; set; } = isOptional;

        public AttendeeResponse? Response { get; } = response;

        public bool IsOrganizer { get; } = isOrganizer;
    }

    private void GuestBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        string text = sender.Text.Trim();
        sender.ItemsSource = text.Length == 0
            ? null
            : _people.Where(p => !_guests.Any(g => string.Equals(g.Email, p.Email, StringComparison.OrdinalIgnoreCase))
                                 && (p.Email.Contains(text, StringComparison.OrdinalIgnoreCase)
                                     || (p.Name?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)))
                .Take(8)
                .Select(p => new PersonSuggestion(p))
                .ToList();
    }

    /// <summary>A suggestion as the box shows it: "Name &lt;email&gt;".</summary>
    private sealed record PersonSuggestion(KnownPerson Person)
    {
        public override string ToString() => Person.Name is { Length: > 0 } name ? $"{name} <{Person.Email}>" : Person.Email;
    }

    private void AddGuest(string text, string? name)
    {
        foreach (var part in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            string email = part.Trim().Trim('<', '>');
            int open = email.LastIndexOf('<');
            if (open >= 0)
            {
                email = email[(open + 1)..];
            }

            if (!email.Contains('@') || email.StartsWith('@') || email.EndsWith('@')
                || _guests.Any(g => string.Equals(g.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var known = _people.FirstOrDefault(p => string.Equals(p.Email, email, StringComparison.OrdinalIgnoreCase));
            _guests.Add(new Guest(email, name ?? known?.Name, false, null));
        }

        _guestBox.Text = string.Empty;
        _guestBox.ItemsSource = null;
        RefreshGuests();
        _ = LoadAvailabilityAsync();
    }

    private void RefreshGuests()
    {
        _guestList.Children.Clear();
        foreach (var guest in _guests)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new TextBlock
            {
                Text = guest.Response switch
                {
                    AttendeeResponse.Accepted => "✓",
                    AttendeeResponse.Declined => "✕",
                    AttendeeResponse.Tentative => "?",
                    _ => "·",
                },
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("TextFillColorSecondaryBrush"),
            });

            var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            who.Children.Add(new TextBlock { Text = guest.Name ?? guest.Email, TextTrimming = TextTrimming.CharacterEllipsis });
            if (guest.Name is not null || guest.IsOrganizer)
            {
                who.Children.Add(new TextBlock
                {
                    Text = guest.IsOrganizer ? $"{guest.Email} · organizer" : guest.Email,
                    Style = EventVisuals.Resource<Style>("CaptionTextBlockStyle"),
                    Foreground = Brush("TextFillColorSecondaryBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }

            Grid.SetColumn(who, 1);
            row.Children.Add(who);

            if (!guest.IsOrganizer)
            {
                var optional = new CheckBox { Content = "Optional", MinWidth = 0, IsChecked = guest.IsOptional };
                optional.Checked += (_, _) => guest.IsOptional = true;
                optional.Unchecked += (_, _) => guest.IsOptional = false;
                Grid.SetColumn(optional, 2);
                row.Children.Add(optional);

                var remove = new Button
                {
                    Content = new FontIcon { Glyph = "", FontSize = 12 },
                    Style = EventVisuals.Resource<Style>("SubtleIconButtonStyle"),
                };
                ToolTipService.SetToolTip(remove, "Remove guest");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, $"Remove {guest.Email}");
                remove.Click += (_, _) =>
                {
                    _guests.Remove(guest);
                    RefreshGuests();
                    _ = LoadAvailabilityAsync();
                };
                Grid.SetColumn(remove, 3);
                row.Children.Add(remove);
            }

            _guestList.Children.Add(row);
        }

        _availabilityLink.Visibility = _guests.Count > 0 && _availability.Visibility == Visibility.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        if (_guests.Count == 0)
        {
            _availability.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Busy times of the user and the guests on the event's day, with the event's time marked.</summary>
    private async Task LoadAvailabilityAsync()
    {
        if (_availability.Visibility != Visibility.Visible || _guests.Count == 0)
        {
            return;
        }

        _availabilityLoad?.Cancel();
        var load = _availabilityLoad = new CancellationTokenSource();
        var day = StartDate;
        var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(day.ToDateTime(TimeOnly.MinValue)));
        var people = new List<(string Email, string Label)> { (Calendar.AccountEmail, "You") };
        people.AddRange(_guests.Select(g => (g.Email, g.Name ?? g.Email)));

        _availability.Children.Clear();
        _availability.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Left });
        var busy = await _edits.GetFreeBusyAsync(Calendar.AccountId, people.Select(p => p.Email).ToList(), from, from.AddDays(1), load.Token);
        if (load.IsCancellationRequested)
        {
            return;
        }

        _availability.Children.Clear();
        if (busy is null)
        {
            _availability.Children.Add(Hint("Availability couldn't be checked right now."));
            return;
        }

        const int firstHour = 7, lastHour = 21;
        const double width = 360, rowHeight = 22;
        double hourWidth = width / (lastHour - firstHour);
        var (start, end) = Times();

        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        var hours = new Canvas { Height = 16, Width = width };
        for (int h = firstHour; h < lastHour; h += 2)
        {
            var label = new TextBlock { Text = TimeText.Hour(h), FontSize = 11, Foreground = Brush("TextFillColorSecondaryBrush") };
            Canvas.SetLeft(label, (h - firstHour) * hourWidth);
            hours.Children.Add(label);
        }

        Grid.SetColumn(hours, 1);
        header.Children.Add(hours);
        _availability.Children.Add(header);

        foreach (var (email, label) in people)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
            row.Children.Add(new TextBlock { Text = label, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });

            var lane = new Canvas { Width = width, Height = rowHeight, Background = Brush("ControlFillColorSecondaryBrush") };
            if (busy.TryGetValue(email, out var person) && !person.IsUnknown)
            {
                foreach (var (from_, to_) in person.Busy)
                {
                    double x1 = Math.Max(0, (from_.LocalDateTime - day.ToDateTime(TimeOnly.MinValue)).TotalHours - firstHour) * hourWidth;
                    double x2 = Math.Min(width, ((to_.LocalDateTime - day.ToDateTime(TimeOnly.MinValue)).TotalHours - firstHour) * hourWidth);
                    if (x2 > x1)
                    {
                        var block = new Rectangle { Width = x2 - x1, Height = rowHeight, Fill = EventVisuals.Tint(Calendar.Color, 200), RadiusX = 2, RadiusY = 2 };
                        Canvas.SetLeft(block, x1);
                        lane.Children.Add(block);
                    }
                }
            }
            else
            {
                var unknown = new TextBlock { Text = "Not visible", FontSize = 11, Foreground = Brush("TextFillColorTertiaryBrush"), Margin = new Thickness(6, 3, 0, 0) };
                lane.Children.Add(unknown);
            }

            if (!start.IsAllDay)
            {
                double s = ((start.DateTime!.Value.LocalDateTime - day.ToDateTime(TimeOnly.MinValue)).TotalHours - firstHour) * hourWidth;
                double e = ((end.DateTime!.Value.LocalDateTime - day.ToDateTime(TimeOnly.MinValue)).TotalHours - firstHour) * hourWidth;
                s = Math.Clamp(s, 0, width);
                e = Math.Clamp(e, 0, width);
                if (e > s)
                {
                    var slot = new Rectangle
                    {
                        Width = e - s,
                        Height = rowHeight,
                        Stroke = Brush("AccentFillColorDefaultBrush"),
                        StrokeThickness = 2,
                        Fill = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
                    };
                    Canvas.SetLeft(slot, s);
                    lane.Children.Add(slot);
                }
            }

            Grid.SetColumn(lane, 1);
            row.Children.Add(lane);
            _availability.Children.Add(row);
        }
    }

    private TextBlock Hint(string text) => new()
    {
        Text = text,
        Style = EventVisuals.Resource<Style>("CaptionTextBlockStyle"),
        Foreground = Brush("TextFillColorSecondaryBrush"),
        TextWrapping = TextWrapping.Wrap,
    };

    // ---- Meet ------------------------------------------------------------------------------------------------

    private void UpdateMeetText()
    {
        bool on = _meet.IsChecked == true;
        string? link = _event?.HangoutLink ?? _event?.Conference?.EntryPoints.FirstOrDefault(p => p.Type == "video")?.Uri;
        _meet.Content = on
            ? _hasMeet && link is not null ? $"Google Meet · {link.Replace("https://", string.Empty, StringComparison.Ordinal)} (click to remove)" : "Google Meet video conferencing will be added"
            : "Add Google Meet video conferencing";
    }

    // ---- Notifications ---------------------------------------------------------------------------------------

    private sealed class ReminderRow
    {
        public required Grid Row { get; init; }

        public required NumberBox Value { get; init; }

        public required ComboBox Unit { get; init; }

        public required ComboBox Method { get; init; }

        public Reminder ToReminder() => new(
            Method.SelectedIndex == 1 ? ReminderMethod.Email : ReminderMethod.Popup,
            (int)Math.Round((double.IsNaN(Value.Value) ? 0 : Value.Value) * ReminderUnits[Math.Max(0, Unit.SelectedIndex)].Minutes));
    }

    private void SetReminders(IEnumerable<Reminder> reminders)
    {
        _reminders.Clear();
        _reminderList.Children.Clear();
        foreach (var r in reminders)
        {
            AddReminderRow(r);
        }
    }

    private void AddReminderRow(Reminder reminder)
    {
        int unit = ReminderUnits.Length - 1;
        while (unit > 0 && reminder.MinutesBefore % ReminderUnits[unit].Minutes != 0)
        {
            unit--;
        }

        var value = new NumberBox
        {
            Minimum = 0,
            Maximum = 40320,
            Value = reminder.MinutesBefore / ReminderUnits[unit].Minutes,
            Width = 96,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        };
        var units = new ComboBox { MinWidth = 110 };
        foreach (var (text, _) in ReminderUnits)
        {
            units.Items.Add(text);
        }

        units.SelectedIndex = unit;
        var method = new ComboBox { MinWidth = 140 };
        method.Items.Add("Notification");
        method.Items.Add("Email");
        method.SelectedIndex = reminder.Method == ReminderMethod.Email ? 1 : 0;

        var remove = new Button { Content = new FontIcon { Glyph = "", FontSize = 12 }, Style = EventVisuals.Resource<Style>("SubtleIconButtonStyle") };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(value, "Notification time");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(units, "Notification time unit");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(method, "Notification type");
        ToolTipService.SetToolTip(remove, "Remove notification");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, "Remove notification");

        var row = new Grid { ColumnSpacing = 8 };
        for (int i = 0; i < 5; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        row.Children.Add(method);
        Grid.SetColumn(value, 1);
        row.Children.Add(value);
        Grid.SetColumn(units, 2);
        row.Children.Add(units);
        var before = new TextBlock { Text = "before", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(before, 3);
        row.Children.Add(before);
        Grid.SetColumn(remove, 4);
        row.Children.Add(remove);

        var entry = new ReminderRow { Row = row, Value = value, Unit = units, Method = method };
        value.ValueChanged += (_, _) => _remindersTouched = true;
        units.SelectionChanged += (_, _) => _remindersTouched = true;
        method.SelectionChanged += (_, _) => _remindersTouched = true;
        remove.Click += (_, _) =>
        {
            _remindersTouched = true;
            _reminders.Remove(entry);
            _reminderList.Children.Remove(row);
        };
        _reminders.Add(entry);
        _reminderList.Children.Add(row);
    }

    // ---- Result ----------------------------------------------------------------------------------------------

    /// <summary>The form's values, to tell what changed.</summary>
    private sealed record Snapshot(
        string Title, EventTime Start, EventTime End, string Zone, IReadOnlyList<string> Recurrence, string Location, bool Meet,
        IReadOnlyList<GuestEdit> Guests, bool CanModify, bool CanInvite, bool CanSee, IReadOnlyList<Reminder> Reminders,
        string ColorId, bool IsBusy, EventVisibility Visibility, string Description, WorkspaceDetails? Workspace);

    private Snapshot Capture()
    {
        var (start, end) = Times();
        return new Snapshot(
            _title.Text.Trim(), start, end, EventZone, Recurrence(start), _location.Text.Trim(), _meet.IsChecked == true,
            _guests.Select(g => new GuestEdit(g.Email, g.IsOptional)).ToList(),
            _canModify.IsChecked == true, _canInvite.IsChecked == true, _canSeeGuests.IsChecked == true,
            _reminders.Select(r => r.ToReminder()).ToList(),
            (_color.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty,
            _busy.SelectedIndex == 0,
            _visibility.SelectedIndex switch { 1 => EventVisibility.Public, 2 => EventVisibility.Private, _ => EventVisibility.Default },
            _description.Text,
            Workspace());
    }

    private WorkspaceDetails? Workspace() => Kind switch
    {
        EventKind.OutOfOffice or EventKind.FocusTime => new WorkspaceDetails(
            _autoDecline.SelectedIndex switch { 1 => AutoDeclineMode.NewInvitations, 2 => AutoDeclineMode.AllConflicting, _ => AutoDeclineMode.None },
            _declineMessage.Text.Trim()),
        EventKind.WorkingLocation => new WorkspaceDetails(
            Location: _workingLocation.SelectedIndex switch { 1 => WorkingLocationType.Office, 2 => WorkingLocationType.Custom, _ => WorkingLocationType.Home },
            LocationLabel: _workingLocation.SelectedIndex > 0 ? _workingLabel.Text.Trim() : null),
        _ => null,
    };

    private EventEdit NewEdit()
    {
        var now = Capture();
        var kind = Kind;
        bool normal = kind == EventKind.Default;
        return new EventEdit
        {
            // Google names untitled out of office and focus time events this way too.
            Summary = now.Title.Length > 0 ? now.Title : kind switch
            {
                EventKind.OutOfOffice => "Out of office",
                EventKind.FocusTime => "Focus time",
                _ => null,
            },
            Start = now.Start,
            End = now.End,
            Recurrence = now.Recurrence.Count > 0 ? now.Recurrence : null,
            Location = normal && now.Location.Length > 0 ? now.Location : null,
            Description = normal && now.Description.Trim().Length > 0 ? now.Description : null,
            Guests = normal && now.Guests.Count > 0 ? now.Guests : null,
            GuestsCanModify = normal && now.CanModify ? true : null,
            GuestsCanInviteOthers = normal && !now.CanInvite ? false : null,
            GuestsCanSeeOtherGuests = normal && !now.CanSee ? false : null,
            Meet = normal && now.Meet ? true : null,
            Reminders = _remindersTouched && kind is EventKind.Default or EventKind.FocusTime ? new ReminderSettings(false, now.Reminders) : null,
            ColorId = now.ColorId.Length > 0 ? now.ColorId : null,
            IsBusy = normal ? now.IsBusy : null,
            Visibility = normal && now.Visibility != EventVisibility.Default ? now.Visibility : null,
            Kind = kind == EventKind.Default ? null : kind,
            Workspace = now.Workspace,
        };
    }

    private EventEdit ChangedEdit()
    {
        var was = _initial;
        var now = Capture();
        bool timesChanged = !SameTime(was.Start, now.Start) || !SameTime(was.End, now.End) || was.Zone != now.Zone;
        bool remindersChanged = _remindersTouched && !was.Reminders.SequenceEqual(now.Reminders);
        return new EventEdit
        {
            Summary = now.Title != was.Title ? now.Title : null,
            Start = timesChanged ? now.Start : null,
            End = timesChanged ? now.End : null,
            Recurrence = now.Recurrence.SequenceEqual(was.Recurrence) ? null : now.Recurrence,
            Location = now.Location != was.Location ? now.Location : null,
            Description = now.Description != was.Description ? now.Description : null,
            Guests = now.Guests.SequenceEqual(was.Guests) ? null : now.Guests,
            GuestsCanModify = now.CanModify != was.CanModify ? now.CanModify : null,
            GuestsCanInviteOthers = now.CanInvite != was.CanInvite ? now.CanInvite : null,
            GuestsCanSeeOtherGuests = now.CanSee != was.CanSee ? now.CanSee : null,
            Meet = now.Meet != was.Meet ? now.Meet : null,
            Reminders = remindersChanged ? new ReminderSettings(false, now.Reminders) : null,
            ColorId = now.ColorId != was.ColorId ? now.ColorId : null,
            IsBusy = now.IsBusy != was.IsBusy ? now.IsBusy : null,
            Visibility = now.Visibility != was.Visibility ? now.Visibility : null,
            Kind = now.Workspace != was.Workspace ? _event!.Kind : null,
            Workspace = now.Workspace != was.Workspace ? now.Workspace : null,
        };
    }

    private static bool SameTime(EventTime a, EventTime b) =>
        a.IsAllDay == b.IsAllDay && (a.IsAllDay ? a.Date == b.Date : a.DateTime!.Value.UtcDateTime == b.DateTime!.Value.UtcDateTime);

    private Brush Brush(string key) => EventVisuals.ThemeResource<Brush>(key, _theme);

    // ---- Time box --------------------------------------------------------------------------------------------

    /// <summary>A time of day: a list in 15-minute steps that also takes a typed time.</summary>
    private sealed class TimeBox
    {
        private bool _setting;

        public TimeBox(TimeOnly value)
        {
            Box = new ComboBox { IsEditable = true, Width = 116, MaxDropDownHeight = 320 };
            for (int i = 0; i < 96; i++)
            {
                Box.Items.Add(Format(TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(15 * i))));
            }

            Set(value);
            Box.SelectionChanged += (_, _) =>
            {
                if (!_setting && Box.SelectedIndex >= 0)
                {
                    Value = TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(15 * Box.SelectedIndex));
                    Changed?.Invoke();
                }
            };
            Box.TextSubmitted += (_, args) =>
            {
                args.Handled = true;
                if (TryParse(args.Text, out var time))
                {
                    Set(time);
                    Changed?.Invoke();
                }
                else
                {
                    Set(Value);
                }
            };
        }

        public event Action? Changed;

        public ComboBox Box { get; }

        public TimeOnly Value { get; private set; }

        public void Set(TimeOnly time)
        {
            _setting = true;
            Value = time;
            int index = time.Minute % 15 == 0 && time.Second == 0 ? time.Hour * 4 + time.Minute / 15 : -1;
            Box.SelectedIndex = index;
            if (index < 0)
            {
                Box.Text = Format(time);
            }

            _setting = false;
        }

        private static string Format(TimeOnly time) => TimeText.Time(DateTime.Today.Add(time.ToTimeSpan()));

        private static bool TryParse(string text, out TimeOnly time)
        {
            text = text.Trim();
            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out var parsed)
                || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.NoCurrentDateDefault, out parsed))
            {
                time = TimeOnly.FromDateTime(parsed);
                return true;
            }

            // "930", "1415", "9": hours and minutes without a separator.
            if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int digits))
            {
                var (hours, minutes) = digits < 100 ? (digits, 0) : (digits / 100, digits % 100);
                if (hours < 24 && minutes < 60)
                {
                    time = new TimeOnly(hours, minutes);
                    return true;
                }
            }

            time = default;
            return false;
        }
    }
}

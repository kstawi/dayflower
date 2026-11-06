// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Calendar;
using Dayflower.Core.Display;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Dayflower.Views;

/// <summary>The day and week views: day headers, an all-day area and a scrolling 24-hour grid.</summary>
public sealed partial class TimeGridView : UserControl
{
    public const double HourHeight = 48;
    private const double GutterWidth = 56;
    private const double AllDayRowHeight = 24;

    /// <summary>Events shorter than this show their title and start time on one line.</summary>
    private static readonly TimeSpan ShortEvent = TimeSpan.FromMinutes(40);

    private readonly List<(FrameworkElement Block, int Day, SlotPlacement Placement)> _blocks = [];
    private readonly List<Grid> _dayLayers = [];
    private readonly DispatcherQueueTimer _clock;
    private IReadOnlyList<DateOnly> _days = [];
    private IReadOnlyList<CalendarItem> _items = [];
    private int _workStart = 8, _workEnd = 18;
    private TimeSpan _joinLead = TimeSpan.FromMinutes(10);
    private FrameworkElement? _nowLine;
    private int _todayIndex = -1;

    // The events showing a Join button in the current build; rebuilt when that set changes.
    private HashSet<CalendarItem> _joinable = [];

    public TimeGridView()
    {
        InitializeComponent();
        Body.Height = 24 * HourHeight;

        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(30);
        _clock.Tick += (_, _) => OnClockTick();
        Loaded += (_, _) => _clock.Start();
        Unloaded += (_, _) => _clock.Stop();
        ActualThemeChanged += (_, _) => Build();
    }

    public event EventHandler<ItemClickedEventArgs>? ItemClicked;

    public event EventHandler<DateOnly>? DayClicked;

    /// <summary>Raised when the user clicks or drags over empty time, to create an event there.</summary>
    public event EventHandler<SlotSelectedEventArgs>? SlotSelected;

    /// <summary>Raised when the user drags an event to a new time or drags its bottom edge.</summary>
    public event EventHandler<ItemMovedEventArgs>? ItemMoved;

    /// <summary>Whether an event can be dragged (the user may edit it, and the app is online).</summary>
    public Func<CalendarItem, bool> CanMove { get; set; } = _ => false;

    /// <summary>Raised when the Join button on an event is clicked.</summary>
    public event EventHandler<CalendarItem>? JoinClicked;

    /// <param name="joinLead">How long before a meeting starts its Join button appears.</param>
    /// <param name="scrollToStart">Scroll to the current time (or the start of the working day).</param>
    public void Show(IReadOnlyList<DateOnly> days, IReadOnlyList<CalendarItem> items, int workStart, int workEnd,
                     TimeSpan joinLead, bool scrollToStart)
    {
        _days = days;
        _items = items;
        _workStart = workStart;
        _workEnd = workEnd;
        _joinLead = joinLead;
        Build();
        if (scrollToStart)
        {
            ScrollToStart();
        }
    }

    private void Build()
    {
        // A rebuild drops any drag or new-event outline with the old elements.
        _press = null;
        _selection = null;
        _selecting = false;

        foreach (var grid in new[] { HeaderRow, AllDayRow, Body })
        {
            grid.Children.Clear();
            grid.RowDefinitions.Clear();
            grid.ColumnDefinitions.Clear();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GutterWidth) });
            foreach (var _ in _days)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        _todayIndex = _days.ToList().IndexOf(today);
        _joinable = JoinableNow();
        BuildHeader(today);
        BuildAllDay();
        BuildBody();
    }

    private void BuildHeader(DateOnly today)
    {
        for (int i = 0; i < _days.Count; i++)
        {
            var day = _days[i];
            bool isToday = day == today;

            var number = new Grid { Width = 32, Height = 32, HorizontalAlignment = HorizontalAlignment.Center };
            if (isToday)
            {
                number.Children.Add(new Ellipse { Fill = ThemeBrush("AccentFillColorDefaultBrush") });
            }

            var numberText = new TextBlock
            {
                Text = day.Day.ToString(System.Globalization.CultureInfo.CurrentCulture),
                FontSize = 18,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (isToday)
            {
                numberText.Foreground = ThemeBrush("TextOnAccentFillColorPrimaryBrush");
            }

            number.Children.Add(numberText);

            var content = new StackPanel { Spacing = 2 };
            content.Children.Add(new TextBlock
            {
                Text = TimeText.DayName(day),
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = ThemeBrush(isToday ? "AccentTextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"),
            });
            content.Children.Add(number);

            var header = new Button
            {
                Content = content,
                Style = EventVisuals.Resource<Style>("SubtleIconButtonStyle"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Padding = new Thickness(12, 4, 12, 4),
            };
            AutomationProperties.SetName(header, TimeText.LongDate(day));
            header.Click += (_, _) => DayClicked?.Invoke(this, day);
            Grid.SetColumn(header, i + 1);
            HeaderRow.Children.Add(header);
        }
    }

    private void BuildAllDay()
    {
        if (_days.Count == 0)
        {
            return;
        }

        var first = _days[0];
        var allDay = _items.Where(i => i.Span.IsAllDayLike)
            .Select(i => (Item: i,
                          First: Math.Max(0, i.Span.FirstDay.DayNumber - first.DayNumber),
                          Last: Math.Min(_days.Count - 1, i.Span.LastDay.DayNumber - first.DayNumber)))
            .Where(x => x.Last >= 0 && x.First < _days.Count)
            .OrderByDescending(x => x.Last - x.First)
            .ThenBy(x => x.Item.Span.Start)
            .ToList();

        var lanes = LaneLayout.Arrange(allDay.Select((x, i) => new DaySpan(i, x.First, x.Last)).ToList());
        int laneCount = lanes.Count == 0 ? 0 : lanes.Max(l => l.Lane) + 1;
        for (int lane = 0; lane < laneCount; lane++)
        {
            AllDayRow.RowDefinitions.Add(new RowDefinition { Height = new GridLength(AllDayRowHeight) });
        }

        // Clicking empty space in a day's all-day area starts an all-day event on that day.
        for (int i = 0; i < _days.Count; i++)
        {
            var day = _days[i];
            var empty = new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), MinHeight = 20 };
            Grid.SetColumn(empty, i + 1);
            Grid.SetRowSpan(empty, Math.Max(1, laneCount));
            empty.Tapped += (_, _) =>
            {
                ClearSelection();
                SlotSelected?.Invoke(this, new SlotSelectedEventArgs(empty, day.ToDateTime(TimeOnly.MinValue), day.AddDays(1).ToDateTime(TimeOnly.MinValue), true));
            };
            AllDayRow.Children.Add(empty);
        }

        foreach (var placement in lanes)
        {
            var (item, firstDay, lastDay) = allDay[placement.Index];
            var block = EventVisuals.Block(item, null, singleLine: true, ActualTheme, OnItemClicked);
            block.Margin = new Thickness(2, 1, 2, 1);
            Grid.SetRow(block, placement.Lane);
            Grid.SetColumn(block, firstDay + 1);
            Grid.SetColumnSpan(block, lastDay - firstDay + 1);
            AllDayRow.Children.Add(block);
        }
    }

    private void BuildBody()
    {
        _blocks.Clear();
        _dayLayers.Clear();
        _nowLine = null;
        int span = Math.Max(_days.Count, 1);

        // Outside working hours is shaded, as in Outlook.
        AddToDays(new Border
        {
            Height = _workStart * HourHeight,
            VerticalAlignment = VerticalAlignment.Top,
            Background = ThemeBrush("SubtleFillColorSecondaryBrush"),
        }, span);
        AddToDays(new Border
        {
            Height = (24 - _workEnd) * HourHeight,
            Margin = new Thickness(0, _workEnd * HourHeight, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Background = ThemeBrush("SubtleFillColorSecondaryBrush"),
        }, span);

        var divider = ThemeBrush("DividerStrokeColorDefaultBrush");
        for (int hour = 1; hour < 24; hour++)
        {
            AddToDays(new Border
            {
                Height = 1,
                Margin = new Thickness(0, hour * HourHeight, 0, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Background = divider,
            }, span);
            Body.Children.Add(new TextBlock
            {
                Text = TimeText.Hour(hour),
                FontSize = 11,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, hour * HourHeight - 8, 8, 0),
            });
        }

        for (int i = 0; i < _days.Count; i++)
        {
            var separator = new Border { BorderBrush = divider, BorderThickness = new Thickness(1, 0, 0, 0) };
            Grid.SetColumn(separator, i + 1);
            Body.Children.Add(separator);

            // A transparent background makes the empty time clickable: click or drag there to create an event.
            var layer = new Grid { Margin = new Thickness(2, 0, 6, 0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            Grid.SetColumn(layer, i + 1);
            Body.Children.Add(layer);
            _dayLayers.Add(layer);
            int dayIndex = i;
            layer.SizeChanged += (_, _) => PositionBlocks(dayIndex);
            layer.PointerPressed += (_, e) => Layer_PointerPressed(dayIndex, layer, e);
            layer.PointerMoved += (_, e) => Layer_PointerMoved(layer, e);
            layer.PointerReleased += (_, e) => Layer_PointerReleased(layer, e);
            layer.PointerCaptureLost += (_, _) => _selecting = false;

            AddDayEvents(i, layer);
        }

        if (_todayIndex >= 0)
        {
            AddNowLine(_dayLayers[_todayIndex]);
        }
    }

    private void AddDayEvents(int dayIndex, Grid layer)
    {
        var dayStart = _days[dayIndex].ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var timed = _items.Where(i => !i.Span.IsAllDayLike && i.Span.Overlaps(dayStart, dayEnd)).ToList();

        var slots = timed.Select((item, index) => new TimeSlot(
            index,
            Math.Max(0, (item.Span.Start - dayStart).TotalMinutes),
            Math.Min(24 * 60, (item.Span.End - dayStart).TotalMinutes))).ToList();

        foreach (var placement in TimeGridLayout.Arrange(slots))
        {
            var item = timed[placement.Index];
            var slot = slots[placement.Index];
            bool isShort = item.Span.End - item.Span.Start < ShortEvent;
            bool joinable = _joinable.Contains(item);
            double inset = joinable ? (isShort ? 30 : 50) : 0;
            var block = isShort
                ? EventVisuals.Block(item, TimeText.Time(item.Span.Start), singleLine: true, ActualTheme, OnItemClicked, inset)
                : EventVisuals.Block(item, TimeText.TimeRange(item.Span.Start, item.Span.End), singleLine: false, ActualTheme, OnItemClicked, inset);

            // The Join button sits on top of the block, not inside it, so the two stay separate buttons; so does the
            // grip for dragging the end.
            FrameworkElement host = block;
            bool movable = CanMove(item);
            if (joinable || movable)
            {
                var grid = new Grid();
                grid.Children.Add(block);
                if (joinable)
                {
                    grid.Children.Add(JoinButton(item, compact: isShort));
                }

                if (movable)
                {
                    var grip = new ResizeGrip();
                    grip.PointerPressed += (_, e) => BeginPress(item, grid, e, resize: true);
                    grip.PointerMoved += (_, e) => ContinuePress(e);
                    grip.PointerReleased += (_, e) => EndPress(e);
                    grip.PointerCaptureLost += (_, _) => CancelPress();
                    grid.Children.Add(grip);
                }

                host = grid;
            }

            if (movable)
            {
                // Buttons handle pointer presses themselves, so the drag listens to handled events too.
                var target = host;
                block.AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) => BeginPress(item, target, e, resize: false)), true);
                block.AddHandler(PointerMovedEvent, new PointerEventHandler((_, e) => ContinuePress(e)), true);
                block.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, e) => EndPress(e)), true);
                block.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => CancelPress()), true);
            }

            host.HorizontalAlignment = HorizontalAlignment.Left;
            host.VerticalAlignment = VerticalAlignment.Top;
            host.Height = Math.Max(slot.EndMinutes - slot.StartMinutes, 20) / 60 * HourHeight - 2;
            host.Margin = new Thickness(0, slot.StartMinutes / 60 * HourHeight + 1, 0, 0);
            layer.Children.Add(host);
            _blocks.Add((host, dayIndex, placement));
        }
    }

    /// <summary>"Join" (or a camera icon on short events) in the accent colour, top right of the event.</summary>
    private Button JoinButton(CalendarItem item, bool compact)
    {
        var button = new Button
        {
            Style = EventVisuals.Resource<Style>("AccentButtonStyle"),
            Content = compact ? new FontIcon { Glyph = "", FontSize = 12 } : new TextBlock { Text = "Join", FontSize = 12 },
            Padding = compact ? new Thickness(6, 0, 6, 0) : new Thickness(10, 2, 10, 2),
            MinWidth = 0,
            MinHeight = 0,
            Height = compact ? 18 : 24,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, compact ? 1 : 4, 4, 0),
        };
        AutomationProperties.SetName(button, $"{item.Meeting!.JoinLabel}: {item.Title}");
        ToolTipService.SetToolTip(button, item.Meeting.JoinLabel);
        button.Click += (_, _) => JoinClicked?.Invoke(this, item);
        return button;
    }

    private HashSet<CalendarItem> JoinableNow()
    {
        var now = DateTime.Now;
        return _items.Where(i => !i.Span.IsAllDayLike && i.IsJoinable(now, _joinLead)).ToHashSet();
    }

    private void OnClockTick()
    {
        // Join buttons appear shortly before a meeting and go when it ends.
        if (!JoinableNow().SetEquals(_joinable))
        {
            Build();
            return;
        }

        UpdateNowLine();
    }

    private void PositionBlocks(int dayIndex)
    {
        double width = _dayLayers[dayIndex].ActualWidth;
        foreach (var (block, day, placement) in _blocks)
        {
            if (day != dayIndex)
            {
                continue;
            }

            double column = width / placement.Columns;
            block.Width = Math.Max(column - 2, 8);
            block.Margin = new Thickness(column * placement.Column, block.Margin.Top, 0, 0);
        }
    }

    private void AddNowLine(Grid layer)
    {
        var accent = ThemeBrush("AccentFillColorDefaultBrush");
        var line = new Grid { VerticalAlignment = VerticalAlignment.Top, Height = 10, Margin = new Thickness(-7, 0, -6, 0), IsHitTestVisible = false };
        line.Children.Add(new Border { Height = 2, Background = accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 0, 0) });
        line.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = accent, HorizontalAlignment = HorizontalAlignment.Left });
        Canvas.SetZIndex(line, 100);
        layer.Children.Add(line);
        _nowLine = line;
        UpdateNowLine();
    }

    private void UpdateNowLine()
    {
        if (_nowLine is null)
        {
            return;
        }

        if (_todayIndex < 0 || _days[_todayIndex] != DateOnly.FromDateTime(DateTime.Now))
        {
            Build(); // the date changed while the view was open
            return;
        }

        _nowLine.Margin = new Thickness(_nowLine.Margin.Left, DateTime.Now.TimeOfDay.TotalHours * HourHeight - 5, _nowLine.Margin.Right, 0);
    }

    private void ScrollToStart()
    {
        double hours = _todayIndex >= 0 ? DateTime.Now.TimeOfDay.TotalHours - 1.5 : _workStart - 0.5;
        double offset = Math.Max(0, hours * HourHeight);
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => Scroller.ChangeView(null, offset, null, disableAnimation: true));
    }

    private void AddToDays(FrameworkElement element, int span)
    {
        Grid.SetColumn(element, 1);
        Grid.SetColumnSpan(element, span);
        Body.Children.Add(element);
    }

    private void OnItemClicked(FrameworkElement anchor, CalendarItem item)
    {
        // The click that ends a drag isn't a click.
        if (_press is { Dragging: true } || Environment.TickCount64 - _dragEndedAt < 400)
        {
            return;
        }

        ItemClicked?.Invoke(this, new ItemClickedEventArgs(anchor, item));
    }

    // ---- Creating by clicking or dragging over empty time -----------------------------------------------------

    private const double SnapMinutes = 15;
    private const double DragThreshold = 6;

    private Border? _selection;
    private bool _selecting;
    private bool _selectionMoved;
    private int _selectionDay;
    private double _selectionAnchor;
    private (double From, double To) _selectionRange;

    /// <summary>Removes the outline of a new event's time (when its flyout closes).</summary>
    public void ClearSelection()
    {
        if (_selection?.Parent is Panel panel)
        {
            panel.Children.Remove(_selection);
        }

        _selection = null;
    }

    private static double Snap(double minutes, Func<double, double> round) => round(minutes / SnapMinutes) * SnapMinutes;

    private void Layer_PointerPressed(int dayIndex, Grid layer, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(layer);
        if (!point.Properties.IsLeftButtonPressed || !e.OriginalSource.Equals(layer))
        {
            return;
        }

        ClearSelection();
        _selecting = true;
        _selectionMoved = false;
        _selectionDay = dayIndex;
        _selectionAnchor = Math.Clamp(Snap(point.Position.Y / HourHeight * 60, Math.Floor), 0, 24 * 60 - SnapMinutes);
        _selectionRange = (_selectionAnchor, _selectionAnchor + SnapMinutes);
        _selection = new Border
        {
            Background = EventVisuals.Tint(((SolidColorBrush)ThemeBrush("AccentFillColorDefaultBrush")).Color, 0x55),
            BorderBrush = ThemeBrush("AccentFillColorDefaultBrush"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Child = new TextBlock { FontSize = 12, Margin = new Thickness(6, 2, 4, 0), TextTrimming = TextTrimming.CharacterEllipsis },
        };
        Canvas.SetZIndex(_selection, 50);
        layer.Children.Add(_selection);
        UpdateSelection();
        layer.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void Layer_PointerMoved(Grid layer, PointerRoutedEventArgs e)
    {
        if (!_selecting)
        {
            return;
        }

        double minutes = Math.Clamp(Snap(e.GetCurrentPoint(layer).Position.Y / HourHeight * 60, Math.Round), 0, 24 * 60);
        var range = minutes <= _selectionAnchor
            ? (Math.Min(minutes, _selectionAnchor), _selectionAnchor + SnapMinutes)
            : (_selectionAnchor, Math.Max(minutes, _selectionAnchor + SnapMinutes));
        if (range != _selectionRange)
        {
            _selectionMoved = true;
            _selectionRange = range;
            UpdateSelection();
        }
    }

    private void Layer_PointerReleased(Grid layer, PointerRoutedEventArgs e)
    {
        if (!_selecting)
        {
            return;
        }

        _selecting = false;
        layer.ReleasePointerCapture(e.Pointer);

        // A click makes an hour-long event, as in Google Calendar; a drag the time dragged over.
        if (!_selectionMoved)
        {
            _selectionRange = (_selectionAnchor, Math.Min(24 * 60, _selectionAnchor + 60));
            UpdateSelection();
        }

        var day = _days[_selectionDay].ToDateTime(TimeOnly.MinValue);
        SlotSelected?.Invoke(this, new SlotSelectedEventArgs(
            _selection!, day.AddMinutes(_selectionRange.From), day.AddMinutes(_selectionRange.To), false));
        e.Handled = true;
    }

    private void UpdateSelection()
    {
        if (_selection is null)
        {
            return;
        }

        var day = _days[_selectionDay].ToDateTime(TimeOnly.MinValue);
        _selection.Margin = new Thickness(0, _selectionRange.From / 60 * HourHeight + 1, 0, 0);
        _selection.Height = Math.Max(12, (_selectionRange.To - _selectionRange.From) / 60 * HourHeight - 2);
        ((TextBlock)_selection.Child).Text = TimeText.TimeRange(day.AddMinutes(_selectionRange.From), day.AddMinutes(_selectionRange.To));
    }

    // ---- Moving and resizing events ---------------------------------------------------------------------------

    /// <summary>An event being pressed on: it becomes a drag once the pointer moves far enough.</summary>
    private sealed class Press
    {
        public required CalendarItem Item { get; init; }

        public required FrameworkElement Host { get; init; }

        public required Windows.Foundation.Point Start { get; init; }

        public required bool Resize { get; init; }

        public bool Dragging { get; set; }

        public Border? Ghost { get; set; }

        public DateTime NewStart { get; set; }

        public DateTime NewEnd { get; set; }
    }

    private Press? _press;
    private long _dragEndedAt;

    /// <summary>Puts a dragged event back where it was (when the change is cancelled or fails).</summary>
    public void CancelDrag()
    {
        if (_press is { } press)
        {
            press.Host.Opacity = 1;
            if (press.Ghost?.Parent is Panel panel)
            {
                panel.Children.Remove(press.Ghost);
            }
        }

        _press = null;
    }

    private void BeginPress(CalendarItem item, FrameworkElement host, PointerRoutedEventArgs e, bool resize)
    {
        var point = e.GetCurrentPoint(Body);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        CancelDrag();
        _press = new Press { Item = item, Host = host, Start = point.Position, Resize = resize, NewStart = item.Span.Start, NewEnd = item.Span.End };
        if (resize)
        {
            ((UIElement)e.OriginalSource).CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void ContinuePress(PointerRoutedEventArgs e)
    {
        if (_press is not { } press)
        {
            return;
        }

        var position = e.GetCurrentPoint(Body).Position;
        if (!press.Dragging)
        {
            if (Math.Abs(position.X - press.Start.X) < DragThreshold && Math.Abs(position.Y - press.Start.Y) < DragThreshold)
            {
                return;
            }

            press.Dragging = true;
            press.Host.Opacity = 0.4;
            press.Ghost = Ghost(press.Item);
            Body.Children.Add(press.Ghost);
        }

        var span = press.Item.Span;
        if (press.Resize)
        {
            // The end follows the pointer, on whichever day it is, at least 15 minutes after the start.
            var end = DayAt(position.X).ToDateTime(TimeOnly.MinValue).AddMinutes(Snap(position.Y / HourHeight * 60, Math.Round));
            press.NewStart = span.Start;
            press.NewEnd = end < span.Start.AddMinutes(SnapMinutes) ? span.Start.AddMinutes(SnapMinutes) : end;
        }
        else
        {
            int days = DayAt(position.X).DayNumber - DayAt(press.Start.X).DayNumber;
            double minutes = Snap((position.Y - press.Start.Y) / HourHeight * 60, Math.Round);
            var shift = TimeSpan.FromDays(days) + TimeSpan.FromMinutes(minutes);
            press.NewStart = span.Start + shift;
            press.NewEnd = span.End + shift;
        }

        PlaceGhost(press);
    }

    private void EndPress(PointerRoutedEventArgs e)
    {
        if (_press is not { } press)
        {
            return;
        }

        if (press.Resize && e.OriginalSource is UIElement source)
        {
            source.ReleasePointerCapture(e.Pointer);
        }

        if (!press.Dragging)
        {
            _press = null;
            return;
        }

        _dragEndedAt = Environment.TickCount64;
        press.Dragging = false;
        if (press.NewStart == press.Item.Span.Start && press.NewEnd == press.Item.Span.End)
        {
            CancelDrag();
            return;
        }

        // The ghost stays until the views reload with the change, or the change is cancelled.
        ItemMoved?.Invoke(this, new ItemMovedEventArgs(press.Item, press.NewStart, press.NewEnd));
        e.Handled = true;
    }

    /// <summary>
    /// The pointer capture was lost. A button gives up its capture just before its PointerReleased reaches the drag,
    /// so this waits to see whether the release follows; only a capture lost for another reason (switching windows
    /// mid-drag) cancels the drag.
    /// </summary>
    private void CancelPress()
    {
        var press = _press;
        if (press is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_press != press)
            {
                return;
            }

            if (press.Dragging)
            {
                _dragEndedAt = Environment.TickCount64;
                CancelDrag();
            }
            else if (press.Ghost is null)
            {
                // A press on the grip that never became a drag.
                _press = null;
            }
        });
    }

    private DateOnly DayAt(double x)
    {
        double columns = Math.Max(1, (Body.ActualWidth - GutterWidth) / Math.Max(1, _days.Count));
        int index = Math.Clamp((int)Math.Floor((x - GutterWidth) / columns), 0, Math.Max(0, _days.Count - 1));
        return _days[index];
    }

    private Border Ghost(CalendarItem item)
    {
        var ghost = new Border
        {
            Background = EventVisuals.Tint(item.Color, 0x99),
            BorderBrush = new SolidColorBrush(item.Color),
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(4),
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            Margin = new Thickness(2, 0, 6, 0),
            Child = new StackPanel
            {
                Padding = new Thickness(6, 2, 4, 0),
                Children =
                {
                    new TextBlock { Text = item.Title, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                    new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis },
                },
            },
        };
        Canvas.SetZIndex(ghost, 60);
        return ghost;
    }

    private void PlaceGhost(Press press)
    {
        var ghost = press.Ghost!;
        var day = DateOnly.FromDateTime(press.NewStart);
        int index = _days.ToList().IndexOf(day);
        ghost.Visibility = index < 0 ? Visibility.Collapsed : Visibility.Visible;
        if (index < 0)
        {
            return;
        }

        double top = press.NewStart.TimeOfDay.TotalHours * HourHeight;
        double bottom = Math.Min(24, (press.NewEnd - day.ToDateTime(TimeOnly.MinValue)).TotalHours) * HourHeight;
        Grid.SetColumn(ghost, index + 1);
        ghost.Margin = new Thickness(2, top + 1, 6, 0);
        ghost.Height = Math.Max(14, bottom - top - 2);
        var lines = ((StackPanel)ghost.Child).Children;
        ((TextBlock)lines[1]).Text = TimeText.TimeRange(press.NewStart, press.NewEnd);
    }

    private Brush ThemeBrush(string key) => EventVisuals.ThemeResource<Brush>(key, ActualTheme);
}

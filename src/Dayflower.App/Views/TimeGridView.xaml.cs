// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Calendar;
using Dayflower.Core.Display;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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

    private readonly List<(Button Block, int Day, SlotPlacement Placement)> _blocks = [];
    private readonly List<Grid> _dayLayers = [];
    private readonly DispatcherQueueTimer _clock;
    private IReadOnlyList<DateOnly> _days = [];
    private IReadOnlyList<CalendarItem> _items = [];
    private int _workStart = 8, _workEnd = 18;
    private FrameworkElement? _nowLine;
    private int _todayIndex = -1;

    public TimeGridView()
    {
        InitializeComponent();
        Body.Height = 24 * HourHeight;

        _clock = DispatcherQueue.CreateTimer();
        _clock.Interval = TimeSpan.FromSeconds(30);
        _clock.Tick += (_, _) => UpdateNowLine();
        Loaded += (_, _) => _clock.Start();
        Unloaded += (_, _) => _clock.Stop();
        ActualThemeChanged += (_, _) => Build();
    }

    public event EventHandler<ItemClickedEventArgs>? ItemClicked;

    public event EventHandler<DateOnly>? DayClicked;

    /// <param name="scrollToStart">Scroll to the current time (or the start of the working day).</param>
    public void Show(IReadOnlyList<DateOnly> days, IReadOnlyList<CalendarItem> items, int workStart, int workEnd, bool scrollToStart)
    {
        _days = days;
        _items = items;
        _workStart = workStart;
        _workEnd = workEnd;
        Build();
        if (scrollToStart)
        {
            ScrollToStart();
        }
    }

    private void Build()
    {
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

            var layer = new Grid { Margin = new Thickness(2, 0, 6, 0) };
            Grid.SetColumn(layer, i + 1);
            Body.Children.Add(layer);
            _dayLayers.Add(layer);
            int dayIndex = i;
            layer.SizeChanged += (_, _) => PositionBlocks(dayIndex);

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
            var block = isShort
                ? EventVisuals.Block(item, TimeText.Time(item.Span.Start), singleLine: true, ActualTheme, OnItemClicked)
                : EventVisuals.Block(item, TimeText.TimeRange(item.Span.Start, item.Span.End), singleLine: false, ActualTheme, OnItemClicked);

            block.HorizontalAlignment = HorizontalAlignment.Left;
            block.VerticalAlignment = VerticalAlignment.Top;
            block.Height = Math.Max(slot.EndMinutes - slot.StartMinutes, 20) / 60 * HourHeight - 2;
            block.Margin = new Thickness(0, slot.StartMinutes / 60 * HourHeight + 1, 0, 0);
            layer.Children.Add(block);
            _blocks.Add((block, dayIndex, placement));
        }
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

    private void OnItemClicked(FrameworkElement anchor, CalendarItem item) => ItemClicked?.Invoke(this, new ItemClickedEventArgs(anchor, item));

    private Brush ThemeBrush(string key) => EventVisuals.ThemeResource<Brush>(key, ActualTheme);
}

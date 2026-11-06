// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using Dayflower.Calendar;
using Dayflower.Core.Display;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Dayflower.Views;

/// <summary>
/// The month view. Each week row stacks its items into lanes: multi-day items first, then all-day items, then timed
/// items by start time. When a day has more than fits, its last line becomes "+N more", which opens that day.
/// </summary>
public sealed partial class MonthView : UserControl
{
    private const double DayNumberHeight = 30;
    private const double LaneHeight = 22;

    private DateOnly _gridStart;
    private int _weeks;
    private int _month;
    private IReadOnlyList<CalendarItem> _items = [];

    public MonthView()
    {
        InitializeComponent();
        Weeks.SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 1)
            {
                Build();
            }
        };
        ActualThemeChanged += (_, _) => Build();
    }

    public event EventHandler<ItemClickedEventArgs>? ItemClicked;

    public event EventHandler<DateOnly>? DayClicked;

    public void Show(DateOnly gridStart, DateOnly gridEnd, int month, IReadOnlyList<CalendarItem> items)
    {
        _gridStart = gridStart;
        _weeks = (gridEnd.DayNumber - gridStart.DayNumber) / 7;
        _month = month;
        _items = items;
        Build();
    }

    private void Build()
    {
        BuildWeekdays();

        Weeks.Children.Clear();
        Weeks.RowDefinitions.Clear();
        double rowHeight = Weeks.ActualHeight > 0 ? Weeks.ActualHeight / Math.Max(_weeks, 1) : 120;
        int lanes = Math.Max(1, (int)((rowHeight - DayNumberHeight - 4) / LaneHeight));

        for (int week = 0; week < _weeks; week++)
        {
            Weeks.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var row = BuildWeek(_gridStart.AddDays(week * 7), lanes);
            Grid.SetRow(row, week);
            Weeks.Children.Add(row);
        }
    }

    private void BuildWeekdays()
    {
        WeekdayRow.Children.Clear();
        WeekdayRow.ColumnDefinitions.Clear();
        for (int i = 0; i < 7; i++)
        {
            WeekdayRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var name = new TextBlock
            {
                Text = TimeText.DayName(_gridStart.AddDays(i)),
                FontSize = 12,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            Grid.SetColumn(name, i);
            WeekdayRow.Children.Add(name);
        }
    }

    private Grid BuildWeek(DateOnly weekStart, int lanes)
    {
        var row = new Grid();
        for (int i = 0; i < 7; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(DayNumberHeight) });
        for (int lane = 0; lane < lanes; lane++)
        {
            row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(LaneHeight) });
        }

        row.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var today = DateOnly.FromDateTime(DateTime.Now);
        var divider = ThemeBrush("DividerStrokeColorDefaultBrush");
        for (int i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i);
            var cell = new Border
            {
                BorderBrush = divider,
                BorderThickness = new Thickness(i == 0 ? 0 : 1, 0, 0, 1),
                Background = day.Month != _month ? ThemeBrush("SubtleFillColorSecondaryBrush") : null,
            };
            Grid.SetColumn(cell, i);
            Grid.SetRowSpan(cell, lanes + 2);
            row.Children.Add(cell);

            var number = DayNumber(day, day == today, day.Month == _month);
            Grid.SetColumn(number, i);
            row.Children.Add(number);
        }

        // Items in this week, in lane priority order.
        var weekEnd = weekStart.AddDays(7);
        var entries = _items
            .Where(item => item.Span.Overlaps(weekStart, weekEnd))
            .Select(item => (Item: item,
                             First: Math.Max(0, item.Span.FirstDay.DayNumber - weekStart.DayNumber),
                             Last: Math.Min(6, item.Span.LastDay.DayNumber - weekStart.DayNumber)))
            .OrderByDescending(e => e.Last - e.First)
            .ThenByDescending(e => e.Item.Span.IsAllDayLike)
            .ThenBy(e => e.Item.Span.Start)
            .ToList();
        var placements = LaneLayout.Arrange(entries.Select((e, i) => new DaySpan(i, e.First, e.Last)).ToList());

        // Count items per day; days with more than fit keep their last lane for "+N more".
        var perDay = new int[7];
        foreach (var e in entries)
        {
            for (int d = e.First; d <= e.Last; d++)
            {
                perDay[d]++;
            }
        }

        var shown = new int[7];
        foreach (var placement in placements)
        {
            var (item, first, last) = entries[placement.Index];
            bool fits = true;
            for (int d = first; d <= last; d++)
            {
                int usable = perDay[d] > lanes ? lanes - 1 : lanes;
                fits &= placement.Lane < usable;
            }

            if (!fits)
            {
                continue;
            }

            for (int d = first; d <= last; d++)
            {
                shown[d]++;
            }

            bool asBar = item.Span.IsAllDayLike || last > first;
            var chip = EventVisuals.Chip(item, asBar, ActualTheme, (anchor, i) => ItemClicked?.Invoke(this, new ItemClickedEventArgs(anchor, i)));
            chip.Margin = new Thickness(3, 1, 3, 1);
            Grid.SetRow(chip, placement.Lane + 1);
            Grid.SetColumn(chip, first);
            Grid.SetColumnSpan(chip, last - first + 1);
            row.Children.Add(chip);
        }

        for (int d = 0; d < 7; d++)
        {
            int hidden = perDay[d] - shown[d];
            if (hidden <= 0)
            {
                continue;
            }

            var day = weekStart.AddDays(d);
            var more = new HyperlinkButton
            {
                Content = $"+{hidden} more",
                FontSize = 12,
                Padding = new Thickness(6, 0, 6, 0),
                Margin = new Thickness(2, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties.SetName(more, $"{hidden} more on {TimeText.LongDate(day)}");
            more.Click += (_, _) => DayClicked?.Invoke(this, day);
            Grid.SetRow(more, lanes);
            Grid.SetColumn(more, d);
            row.Children.Add(more);
        }

        return row;
    }

    private Button DayNumber(DateOnly day, bool isToday, bool inMonth)
    {
        var number = new Grid { Width = 24, Height = 24 };
        if (isToday)
        {
            number.Children.Add(new Ellipse { Fill = ThemeBrush("AccentFillColorDefaultBrush") });
        }

        number.Children.Add(new TextBlock
        {
            Text = day.Day == 1 ? TimeText.ShortDate(day) : day.Day.ToString(CultureInfo.CurrentCulture),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = isToday
                ? ThemeBrush("TextOnAccentFillColorPrimaryBrush")
                : ThemeBrush(inMonth ? "TextFillColorPrimaryBrush" : "TextFillColorTertiaryBrush"),
        });
        if (day.Day == 1 && !isToday)
        {
            number.Width = double.NaN;
        }

        var button = new Button
        {
            Content = number,
            Style = EventVisuals.Resource<Style>("SubtleIconButtonStyle"),
            Padding = new Thickness(4, 0, 4, 0),
            MinHeight = 24,
            Margin = new Thickness(4, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        AutomationProperties.SetName(button, TimeText.LongDate(day));
        button.Click += (_, _) => DayClicked?.Invoke(this, day);
        return button;
    }

    private Brush ThemeBrush(string key) => EventVisuals.ThemeResource<Brush>(key, ActualTheme);
}

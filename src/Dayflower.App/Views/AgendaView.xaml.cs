// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Calendar;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Dayflower.Views;

/// <summary>A day heading in the agenda list.</summary>
public sealed record AgendaHeader(string Text, Brush Brush)
{
    public override string ToString() => Text;
}

/// <summary>One event row in the agenda list.</summary>
public sealed class AgendaEntry(CalendarItem item, string time)
{
    public CalendarItem Item { get; } = item;

    public string Time { get; } = time;

    public string Title => Item.Title;

    public string Detail { get; } = string.Join(" · ", new[] { item.Event.Location, item.Copies[0].CalendarName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public Visibility HasDetail => string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;

    public SolidColorBrush ColorBrush { get; } = new(item.Color);

    /// <summary>What a screen reader says for the row.</summary>
    public override string ToString() => string.IsNullOrEmpty(Detail) ? $"{Title}, {Time}" : $"{Title}, {Time}, {Detail}";
}

public sealed partial class AgendaTemplateSelector : DataTemplateSelector
{
    public DataTemplate? HeaderTemplate { get; set; }

    public DataTemplate? EntryTemplate { get; set; }

    protected override DataTemplate? SelectTemplateCore(object item) => item is AgendaHeader ? HeaderTemplate : EntryTemplate;

    protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container) => SelectTemplateCore(item);
}

/// <summary>The agenda view: upcoming events grouped by day, days without events left out.</summary>
public sealed partial class AgendaView : UserControl
{
    public AgendaView()
    {
        InitializeComponent();
    }

    public event EventHandler<ItemClickedEventArgs>? ItemClicked;

    public void Show(DateOnly first, DateOnly afterLast, IReadOnlyList<CalendarItem> items)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var rows = new List<object>();
        for (var day = first; day < afterLast; day = day.AddDays(1))
        {
            var dayStart = day.ToDateTime(TimeOnly.MinValue);
            var onDay = items
                .Where(i => i.Span.Overlaps(dayStart, dayStart.AddDays(1)))
                .OrderByDescending(i => i.Span.IsAllDayLike)
                .ThenBy(i => i.Span.Start)
                .ToList();
            if (onDay.Count == 0)
            {
                continue;
            }

            string heading = day == today ? $"Today · {TimeText.LongDate(day)}"
                : day == today.AddDays(1) ? $"Tomorrow · {TimeText.LongDate(day)}"
                : TimeText.LongDate(day);
            rows.Add(new AgendaHeader(heading, ThemeBrush(day == today ? "AccentTextFillColorPrimaryBrush" : "TextFillColorPrimaryBrush")));

            foreach (var item in onDay)
            {
                rows.Add(new AgendaEntry(item, TimeLabel(item, dayStart)));
            }
        }

        List.ItemsSource = rows;
        Empty.Text = "Nothing scheduled in these 30 days.";
        Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Search results in the order given (upcoming soonest first, then past ones latest first), under day headings,
    /// with a "Past events" heading where the past ones begin.
    /// </summary>
    public void ShowResults(IReadOnlyList<CalendarItem> items, string emptyText)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);
        var rows = new List<object>();
        DateOnly? lastDay = null;
        bool inPast = false;
        foreach (var item in items)
        {
            if (!inPast && item.Span.End <= now)
            {
                inPast = true;
                lastDay = null;
                if (rows.Count > 0)
                {
                    rows.Add(new AgendaHeader("Past events", ThemeBrush("TextFillColorSecondaryBrush")));
                }
            }

            var day = item.Span.FirstDay;
            if (day != lastDay)
            {
                lastDay = day;
                string date = day.Year == today.Year ? TimeText.LongDate(day) : $"{TimeText.LongDate(day)} {day.Year}";
                string heading = day == today ? $"Today · {date}" : day == today.AddDays(1) ? $"Tomorrow · {date}" : date;
                rows.Add(new AgendaHeader(heading, ThemeBrush(day == today ? "AccentTextFillColorPrimaryBrush" : "TextFillColorPrimaryBrush")));
            }

            rows.Add(new AgendaEntry(item, item.Span.IsAllDayLike
                ? item.Span.LastDay > item.Span.FirstDay ? $"Until {TimeText.ShortDate(item.Span.LastDay)}" : "All day"
                : TimeLabel(item, day.ToDateTime(TimeOnly.MinValue))));
        }

        List.ItemsSource = rows;
        Empty.Text = emptyText;
        Empty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string TimeLabel(CalendarItem item, DateTime dayStart)
    {
        if (item.Span.IsAllDayLike)
        {
            return "All day";
        }

        // Clip events that cross midnight to this day.
        var start = item.Span.Start < dayStart ? dayStart : item.Span.Start;
        var end = item.Span.End > dayStart.AddDays(1) ? dayStart.AddDays(1) : item.Span.End;
        return TimeText.TimeRange(start, end);
    }

    private void List_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AgendaEntry entry && List.ContainerFromItem(entry) is FrameworkElement container)
        {
            ItemClicked?.Invoke(this, new ItemClickedEventArgs(container, entry.Item));
        }
    }

    private Brush ThemeBrush(string key) => EventVisuals.ThemeResource<Brush>(key, ActualTheme);
}

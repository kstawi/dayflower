// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using Dayflower.Calendar;

namespace Dayflower.Views;

/// <summary>
/// Times and dates as the views show them: 12/24-hour style from the Windows regional format, month and day names in
/// the app's language (English), like the period titles.
/// </summary>
internal static class TimeText
{
    private static readonly CultureInfo Names = CultureInfo.CurrentUICulture;
    private static readonly bool Uses24Hour = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('H', StringComparison.Ordinal);
    private static readonly bool DayBeforeMonth = DayFirst(CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern);

    public static string Time(DateTime t) => Uses24Hour
        ? t.ToString("HH:mm", CultureInfo.InvariantCulture)
        : t.ToString(t.Minute == 0 ? "h tt" : "h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>Hour labels for the time grid: "13:00" or "1 PM".</summary>
    public static string Hour(int hour) => Uses24Hour
        ? $"{hour:00}:00"
        : new DateTime(2000, 1, 1, hour, 0, 0).ToString("h tt", CultureInfo.InvariantCulture);

    public static string TimeRange(DateTime start, DateTime end) => $"{Time(start)} – {Time(end)}";

    public static string DayName(DateOnly day) => day.ToString("ddd", Names);

    /// <summary>"Friday, 6 November" (or "Friday, November 6"), with the year when it isn't this year.</summary>
    public static string LongDate(DateOnly day)
    {
        string format = DayBeforeMonth ? "dddd, d MMMM" : "dddd, MMMM d";
        if (day.Year != DateTime.Today.Year)
        {
            format += DayBeforeMonth ? " yyyy" : ", yyyy";
        }

        return day.ToString(format, Names);
    }

    /// <summary>"6 Nov" or "Nov 6".</summary>
    public static string ShortDate(DateOnly day) => day.ToString(DayBeforeMonth ? "d MMM" : "MMM d", Names);

    /// <summary>When an item happens, in one line for lists and flyouts.</summary>
    public static string When(CalendarItem item)
    {
        var span = item.Span;
        if (span.IsAllDay)
        {
            return span.FirstDay == span.LastDay
                ? $"{LongDate(span.FirstDay)} · All day"
                : $"{ShortDate(span.FirstDay)} – {ShortDate(span.LastDay)} · All day";
        }

        return span.FirstDay == span.LastDay
            ? $"{LongDate(span.FirstDay)} · {TimeRange(span.Start, span.End)}"
            : $"{ShortDate(span.FirstDay)}, {Time(span.Start)} – {ShortDate(span.LastDay)}, {Time(span.End)}";
    }

    private static bool DayFirst(string pattern)
    {
        int d = pattern.IndexOf('d'), m = pattern.IndexOf('M');
        return d >= 0 && m >= 0 && d < m;
    }
}

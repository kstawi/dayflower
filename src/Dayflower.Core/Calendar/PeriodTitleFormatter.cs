// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;

namespace Dayflower.Core.Calendar;

/// <summary>
/// Formats the heading above a calendar view: "Friday, 6 November 2026", "November 2026" or "Oct – Nov 2026".
/// </summary>
/// <remarks>
/// Month and day names come from the app's display language, while the order of day and month follows the
/// Windows regional format. An English app on a PC with Polish regional settings shows "Friday, 6 November 2026",
/// not "piątek, 6 listopada 2026" and not "Friday, November 6, 2026".
/// </remarks>
public sealed class PeriodTitleFormatter
{
    private const string RangeSeparator = " – ";

    private readonly CultureInfo _names;
    private readonly bool _dayBeforeMonth;

    public PeriodTitleFormatter(CultureInfo displayLanguage, CultureInfo regionalFormat)
    {
        _names = displayLanguage;
        string pattern = regionalFormat.DateTimeFormat.ShortDatePattern;
        int day = pattern.IndexOf('d');
        int month = pattern.IndexOf('M');
        _dayBeforeMonth = day >= 0 && month >= 0 && day < month;
    }

    public string Format(CalendarPeriod period) => period.Kind switch
    {
        CalendarViewKind.Day => FormatDay(period.Start),
        CalendarViewKind.Month => FormatDate(period.Start, "MMMM yyyy"),
        _ => FormatRange(period.Start, period.End.AddDays(-1)),
    };

    private string FormatDay(DateOnly date) =>
        FormatDate(date, _dayBeforeMonth ? "dddd, d MMMM yyyy" : "dddd, MMMM d, yyyy");

    private string FormatRange(DateOnly first, DateOnly last)
    {
        if (first.Year == last.Year && first.Month == last.Month)
        {
            return FormatDate(first, "MMMM yyyy");
        }

        if (first.Year == last.Year)
        {
            return FormatDate(first, "MMM") + RangeSeparator + FormatDate(last, "MMM yyyy");
        }

        return FormatDate(first, "MMM yyyy") + RangeSeparator + FormatDate(last, "MMM yyyy");
    }

    private string FormatDate(DateOnly date, string format) => date.ToString(format, _names);
}

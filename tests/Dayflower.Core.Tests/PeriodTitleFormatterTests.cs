// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using Dayflower.Core.Calendar;

namespace Dayflower.Core.Tests;

public class PeriodTitleFormatterTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");

    private static string Title(CalendarViewKind kind, DateOnly anchor, CultureInfo regional) =>
        new PeriodTitleFormatter(English, regional).Format(new CalendarPeriod(kind, anchor, DayOfWeek.Monday));

    [Fact]
    public void Day_uses_english_names_in_polish_day_month_order()
    {
        Assert.Equal("Friday, 6 November 2026", Title(CalendarViewKind.Day, new DateOnly(2026, 11, 6), Polish));
    }

    [Fact]
    public void Day_uses_month_first_order_for_us_regional_format()
    {
        Assert.Equal("Friday, November 6, 2026", Title(CalendarViewKind.Day, new DateOnly(2026, 11, 6), English));
    }

    [Fact]
    public void Month_shows_month_and_year()
    {
        Assert.Equal("November 2026", Title(CalendarViewKind.Month, new DateOnly(2026, 11, 20), Polish));
    }

    [Fact]
    public void Week_within_one_month_shows_that_month()
    {
        // Monday 2 – Sunday 8 November
        Assert.Equal("November 2026", Title(CalendarViewKind.Week, new DateOnly(2026, 11, 6), Polish));
    }

    [Fact]
    public void Week_across_two_months_shows_both()
    {
        // Monday 26 October – Sunday 1 November
        Assert.Equal("Oct – Nov 2026", Title(CalendarViewKind.Week, new DateOnly(2026, 10, 28), Polish));
    }

    [Fact]
    public void Week_across_new_year_shows_both_years()
    {
        // Monday 28 December 2026 – Sunday 3 January 2027
        Assert.Equal("Dec 2026 – Jan 2027", Title(CalendarViewKind.Week, new DateOnly(2026, 12, 30), Polish));
    }
}

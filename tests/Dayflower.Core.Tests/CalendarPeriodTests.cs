// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Calendar;

namespace Dayflower.Core.Tests;

public class CalendarPeriodTests
{
    // Friday 6 November 2026
    private static readonly DateOnly Friday = new(2026, 11, 6);

    [Theory]
    [InlineData(DayOfWeek.Monday, 2026, 11, 2)]
    [InlineData(DayOfWeek.Sunday, 2026, 11, 1)]
    [InlineData(DayOfWeek.Saturday, 2026, 10, 31)]
    [InlineData(DayOfWeek.Friday, 2026, 11, 6)]
    public void Week_starts_on_the_regional_first_day(DayOfWeek firstDay, int year, int month, int day)
    {
        var period = new CalendarPeriod(CalendarViewKind.Week, Friday, firstDay);

        Assert.Equal(new DateOnly(year, month, day), period.Start);
        Assert.Equal(period.Start.AddDays(7), period.End);
    }

    [Fact]
    public void Day_covers_one_day()
    {
        var period = new CalendarPeriod(CalendarViewKind.Day, Friday, DayOfWeek.Monday);

        Assert.Equal(Friday, period.Start);
        Assert.Equal(Friday.AddDays(1), period.End);
    }

    [Fact]
    public void Month_covers_the_calendar_month()
    {
        var period = new CalendarPeriod(CalendarViewKind.Month, Friday, DayOfWeek.Monday);

        Assert.Equal(new DateOnly(2026, 11, 1), period.Start);
        Assert.Equal(new DateOnly(2026, 12, 1), period.End);
    }

    [Fact]
    public void Agenda_starts_on_the_anchor()
    {
        var period = new CalendarPeriod(CalendarViewKind.Agenda, Friday, DayOfWeek.Monday);

        Assert.Equal(Friday, period.Start);
        Assert.Equal(Friday.AddDays(CalendarPeriod.AgendaDays), period.End);
    }

    [Theory]
    [InlineData(CalendarViewKind.Day, 2026, 11, 7)]
    [InlineData(CalendarViewKind.Week, 2026, 11, 13)]
    [InlineData(CalendarViewKind.Month, 2026, 12, 6)]
    [InlineData(CalendarViewKind.Agenda, 2026, 12, 6)]
    public void Next_moves_the_anchor_by_one_view(CalendarViewKind kind, int year, int month, int day)
    {
        var next = new CalendarPeriod(kind, Friday, DayOfWeek.Monday).Next();

        Assert.Equal(new DateOnly(year, month, day), next.Anchor);
        Assert.Equal(kind, next.Kind);
    }

    [Fact]
    public void Previous_undoes_next()
    {
        var period = new CalendarPeriod(CalendarViewKind.Week, Friday, DayOfWeek.Monday);

        Assert.Equal(period, period.Next().Previous());
    }

    [Fact]
    public void Next_month_from_the_31st_lands_on_the_last_day()
    {
        var period = new CalendarPeriod(CalendarViewKind.Month, new DateOnly(2027, 1, 31), DayOfWeek.Monday);

        Assert.Equal(new DateOnly(2027, 2, 28), period.Next().Anchor);
    }

    [Fact]
    public void Switching_view_keeps_the_anchor()
    {
        var week = new CalendarPeriod(CalendarViewKind.Month, Friday, DayOfWeek.Monday).WithKind(CalendarViewKind.Week);

        Assert.Equal(Friday, week.Anchor);
        Assert.True(week.Contains(Friday));
    }

    [Fact]
    public void Contains_excludes_the_end()
    {
        var period = new CalendarPeriod(CalendarViewKind.Day, Friday, DayOfWeek.Monday);

        Assert.True(period.Contains(Friday));
        Assert.False(period.Contains(Friday.AddDays(1)));
        Assert.False(period.Contains(Friday.AddDays(-1)));
    }
}

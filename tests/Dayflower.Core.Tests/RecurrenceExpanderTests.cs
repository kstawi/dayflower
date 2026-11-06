// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Recurrence;
using static Dayflower.Core.Tests.TestEvents;

namespace Dayflower.Core.Tests;

public class RecurrenceExpanderTests
{
    [Fact]
    public void Weekly_meeting_keeps_its_local_time_across_the_october_clock_change()
    {
        // Mondays and Wednesdays 09:00-09:30 in Warsaw. Clocks go back on Sunday 25 October 2026.
        var series = Timed("standup", "2026-10-19T09:00:00+02:00", "2026-10-19T09:30:00+02:00", Warsaw,
                           "RRULE:FREQ=WEEKLY;BYDAY=MO,WE");

        var occurrences = RecurrenceExpander.Expand(series, Utc(2026, 10, 19), Utc(2026, 11, 2));

        Assert.Equal(
            [Utc(2026, 10, 19, 7), Utc(2026, 10, 21, 7), Utc(2026, 10, 26, 8), Utc(2026, 10, 28, 8)],
            occurrences.Select(o => o.Start.DateTime!.Value.ToUniversalTime()));
        Assert.All(occurrences, o => Assert.Equal(9, o.Start.DateTime!.Value.Hour));
        Assert.Equal(TimeSpan.FromHours(1), occurrences[^1].Start.DateTime!.Value.Offset);
        Assert.All(occurrences, o => Assert.Equal(TimeSpan.FromMinutes(30), o.End.DateTime!.Value - o.Start.DateTime!.Value));
    }

    [Fact]
    public void Excluded_dates_are_skipped()
    {
        var series = Timed("daily", "2026-11-06T09:00:00+01:00", "2026-11-06T10:00:00+01:00", Warsaw,
                           "RRULE:FREQ=DAILY;COUNT=5", "EXDATE;TZID=Europe/Warsaw:20261108T090000");

        var occurrences = RecurrenceExpander.Expand(series, Utc(2026, 11, 1), Utc(2026, 12, 1));

        Assert.Equal([6, 7, 9, 10], occurrences.Select(o => o.Start.DateTime!.Value.Day));
    }

    [Fact]
    public void Until_in_utc_ends_the_series()
    {
        var series = Timed("weekly", "2026-11-02T15:00:00+01:00", "2026-11-02T16:00:00+01:00", Warsaw,
                           "RRULE:FREQ=WEEKLY;UNTIL=20261116T225959Z");

        var occurrences = RecurrenceExpander.Expand(series, Utc(2026, 11, 1), Utc(2027, 1, 1));

        Assert.Equal([2, 9, 16], occurrences.Select(o => o.Start.DateTime!.Value.Day));
    }

    [Fact]
    public void Yearly_all_day_event_repeats_on_its_date()
    {
        var birthday = AllDay("birthday", new DateOnly(2020, 3, 15), new DateOnly(2020, 3, 16), "RRULE:FREQ=YEARLY");

        var occurrences = RecurrenceExpander.Expand(birthday, Utc(2026, 1, 1), Utc(2027, 1, 1));

        var only = Assert.Single(occurrences);
        Assert.Equal(new DateOnly(2026, 3, 15), only.Start.Date);
        Assert.Equal(new DateOnly(2026, 3, 16), only.End.Date);
    }

    [Fact]
    public void Last_friday_of_the_month()
    {
        var series = Timed("review", "2026-10-30T14:00:00+01:00", "2026-10-30T15:00:00+01:00", Warsaw,
                           "RRULE:FREQ=MONTHLY;BYDAY=-1FR");

        var occurrences = RecurrenceExpander.Expand(series, Utc(2026, 10, 1), Utc(2027, 1, 1));

        Assert.Equal(
            [new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 27), new DateOnly(2026, 12, 25)],
            occurrences.Select(o => DateOnly.FromDateTime(o.Start.DateTime!.Value.DateTime)));
    }

    [Fact]
    public void Occurrence_that_started_before_the_range_but_is_still_going_is_included()
    {
        // Every night 23:00-01:00 UTC; the range starts at midnight.
        var series = Timed("night", "2026-11-01T23:00:00Z", "2026-11-02T01:00:00Z", "UTC", "RRULE:FREQ=DAILY");

        var occurrences = RecurrenceExpander.Expand(series, Utc(2026, 11, 5), Utc(2026, 11, 6));

        Assert.Equal([Utc(2026, 11, 4, 23), Utc(2026, 11, 5, 23)], occurrences.Select(o => o.Start.DateTime!.Value));
    }

    [Fact]
    public void Infinite_series_is_limited_to_the_range()
    {
        var series = Timed("forever", "2020-01-01T08:00:00+01:00", "2020-01-01T08:15:00+01:00", Warsaw, "RRULE:FREQ=DAILY");

        var occurrences = RecurrenceExpander.Expand(series, Utc(2026, 11, 1), Utc(2026, 11, 8));

        Assert.Equal(7, occurrences.Count);
    }

    [Fact]
    public void Event_without_recurrence_has_no_occurrences()
    {
        var single = Timed("once", "2026-11-06T09:00:00+01:00", "2026-11-06T10:00:00+01:00");

        Assert.Empty(RecurrenceExpander.Expand(single, Utc(2026, 11, 1), Utc(2026, 12, 1)));
    }
}

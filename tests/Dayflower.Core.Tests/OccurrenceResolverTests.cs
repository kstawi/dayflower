// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Model;
using Dayflower.Core.Recurrence;
using static Dayflower.Core.Tests.TestEvents;

namespace Dayflower.Core.Tests;

public class OccurrenceResolverTests
{
    private static readonly CalendarEvent Daily = Timed(
        "daily", "2026-11-02T09:00:00+01:00", "2026-11-02T09:15:00+01:00", Warsaw, "RRULE:FREQ=DAILY;COUNT=5");

    [Fact]
    public void Moved_occurrence_replaces_the_scheduled_one()
    {
        var moved = Override(Daily, "daily_20261104", "2026-11-04T09:00:00+01:00",
                             "2026-11-04T11:00:00+01:00", "2026-11-04T11:30:00+01:00");

        var result = OccurrenceResolver.Resolve([Daily, moved], Utc(2026, 11, 2), Utc(2026, 11, 7));

        Assert.Equal(5, result.Count);
        var wednesday = Assert.Single(result, o => o.Start.DateTime!.Value.Day == 4);
        Assert.Same(moved, wednesday.Event);
        Assert.Equal(11, wednesday.Start.DateTime!.Value.Hour);
        Assert.Equal("2026-11-04T08:00:00Z", wednesday.OriginalStart!.OccurrenceKey);
    }

    [Fact]
    public void Cancelled_occurrence_disappears()
    {
        var cancelled = Override(Daily, "daily_20261103", "2026-11-03T09:00:00+01:00",
                                 "2026-11-03T09:00:00+01:00", "2026-11-03T09:15:00+01:00", EventStatus.Cancelled);

        var result = OccurrenceResolver.Resolve([Daily, cancelled], Utc(2026, 11, 2), Utc(2026, 11, 7));

        Assert.Equal([2, 4, 5, 6], result.Select(o => o.Start.DateTime!.Value.Day));
    }

    [Fact]
    public void Occurrence_moved_out_of_the_range_shows_in_neither_place()
    {
        var movedAway = Override(Daily, "daily_20261102", "2026-11-02T09:00:00+01:00",
                                 "2026-11-20T09:00:00+01:00", "2026-11-20T09:15:00+01:00");

        var result = OccurrenceResolver.Resolve([Daily, movedAway], Utc(2026, 11, 2), Utc(2026, 11, 3));

        Assert.Empty(result);
    }

    [Fact]
    public void One_off_events_are_filtered_to_the_range_and_cancelled_ones_dropped()
    {
        var inside = Timed("inside", "2026-11-06T09:00:00+01:00", "2026-11-06T10:00:00+01:00");
        var outside = Timed("outside", "2026-11-16T09:00:00+01:00", "2026-11-16T10:00:00+01:00");
        var cancelled = Timed("cancelled", "2026-11-06T12:00:00+01:00", "2026-11-06T13:00:00+01:00") with
        {
            Status = EventStatus.Cancelled,
        };

        var result = OccurrenceResolver.Resolve([inside, outside, cancelled], Utc(2026, 11, 2), Utc(2026, 11, 9));

        Assert.Same(inside, Assert.Single(result).Event);
    }

    [Fact]
    public void Results_are_in_start_order()
    {
        var late = Timed("late", "2026-11-02T18:00:00+01:00", "2026-11-02T19:00:00+01:00");
        var early = Timed("early", "2026-11-02T07:00:00+01:00", "2026-11-02T08:00:00+01:00");

        var result = OccurrenceResolver.Resolve([late, Daily, early], Utc(2026, 11, 2), Utc(2026, 11, 3));

        Assert.Equal(["early", "daily", "late"], result.Select(o => o.Event.Key.EventId));
    }
}

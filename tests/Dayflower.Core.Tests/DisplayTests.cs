// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Calendar;
using Dayflower.Core.Display;
using Dayflower.Core.Model;
using Dayflower.Core.Recurrence;
using static Dayflower.Core.Tests.TestEvents;

namespace Dayflower.Core.Tests;

public class CalendarGridTests
{
    [Fact]
    public void Month_grid_shows_whole_weeks()
    {
        // November 2026 starts on a Sunday and ends on a Monday.
        var period = new CalendarPeriod(CalendarViewKind.Month, new DateOnly(2026, 11, 6), DayOfWeek.Monday);

        Assert.Equal(new DateOnly(2026, 10, 26), period.GridStart);
        Assert.Equal(new DateOnly(2026, 12, 7), period.GridEnd);
    }

    [Fact]
    public void Other_views_show_their_own_dates()
    {
        var week = new CalendarPeriod(CalendarViewKind.Week, new DateOnly(2026, 11, 6), DayOfWeek.Monday);

        Assert.Equal(week.Start, week.GridStart);
        Assert.Equal(week.End, week.GridEnd);
    }
}

public class LocalSpanTests
{
    private static readonly TimeZoneInfo WarsawZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    [Fact]
    public void Timed_event_is_shown_in_the_viewers_zone()
    {
        var span = LocalSpan.Of(EventTime.At(At("2026-11-06T15:00:00Z")), EventTime.At(At("2026-11-06T16:00:00Z")), WarsawZone);

        Assert.Equal(new DateTime(2026, 11, 6, 16, 0, 0), span.Start);
        Assert.Equal(new DateTime(2026, 11, 6, 17, 0, 0), span.End);
        Assert.False(span.IsAllDayLike);
    }

    [Fact]
    public void All_day_event_covers_its_days()
    {
        var span = LocalSpan.Of(EventTime.AllDay(new DateOnly(2026, 11, 9)), EventTime.AllDay(new DateOnly(2026, 11, 11)), WarsawZone);

        Assert.True(span.IsAllDayLike);
        Assert.Equal(new DateOnly(2026, 11, 9), span.FirstDay);
        Assert.Equal(new DateOnly(2026, 11, 10), span.LastDay);
        Assert.True(span.Overlaps(new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 11)));
        Assert.False(span.Overlaps(new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 12)));
    }

    [Fact]
    public void Timed_event_of_a_day_or_more_goes_in_the_all_day_area()
    {
        var span = LocalSpan.Of(EventTime.At(At("2026-11-20T00:00:00+01:00")), EventTime.At(At("2026-11-21T00:00:00+01:00")), WarsawZone);

        Assert.True(span.IsAllDayLike);
        Assert.Equal(span.FirstDay, span.LastDay);
    }

    [Fact]
    public void Event_ending_at_midnight_doesnt_touch_the_next_day()
    {
        var span = LocalSpan.Of(EventTime.At(At("2026-11-06T22:00:00+01:00")), EventTime.At(At("2026-11-07T00:00:00+01:00")), WarsawZone);

        Assert.Equal(new DateOnly(2026, 11, 6), span.LastDay);
    }

    [Fact]
    public void Local_midnight_converts_to_utc()
    {
        Assert.Equal(At("2026-11-05T23:00:00Z"), LocalDays.StartUtc(new DateOnly(2026, 11, 6), WarsawZone));
        Assert.Equal(At("2026-07-05T22:00:00Z"), LocalDays.StartUtc(new DateOnly(2026, 7, 6), WarsawZone));
    }
}

public class TimeGridLayoutTests
{
    private static Dictionary<int, (int Column, int Columns)> Arrange(params (double Start, double End)[] slots) =>
        TimeGridLayout.Arrange(slots.Select((s, i) => new TimeSlot(i, s.Start, s.End)).ToList())
            .ToDictionary(p => p.Index, p => (p.Column, p.Columns));

    [Fact]
    public void Separate_events_take_the_full_width()
    {
        var result = Arrange((540, 600), (600, 660));

        Assert.Equal((0, 1), result[0]);
        Assert.Equal((0, 1), result[1]);
    }

    [Fact]
    public void Clashing_events_share_the_width()
    {
        var result = Arrange((540, 600), (570, 630), (600, 660));

        Assert.Equal((0, 2), result[0]);
        Assert.Equal((1, 2), result[1]);
        Assert.Equal((0, 2), result[2]); // reuses the first column once the first event ends
    }

    [Fact]
    public void Chain_of_overlaps_forms_one_group()
    {
        var result = Arrange((540, 660), (560, 580), (600, 620), (610, 640));

        Assert.All(result.Values, p => Assert.Equal(3, p.Columns));
        Assert.Equal(0, result[0].Column);
        Assert.Equal(1, result[1].Column);
        Assert.Equal(1, result[2].Column);
        Assert.Equal(2, result[3].Column);
    }

    [Fact]
    public void Very_short_events_count_with_their_drawn_size()
    {
        // A 5-minute event is drawn 20 minutes tall, so the one starting 10 minutes later can't share its column.
        var result = Arrange((540, 545), (550, 600));

        Assert.Equal(2, result[0].Columns);
    }
}

public class LaneLayoutTests
{
    [Fact]
    public void Items_take_the_first_free_lane_in_the_order_given()
    {
        var result = LaneLayout.Arrange(
        [
            new DaySpan(0, 0, 2),
            new DaySpan(1, 1, 1),
            new DaySpan(2, 3, 4),
            new DaySpan(3, 2, 3),
        ]).ToDictionary(p => p.Index, p => p.Lane);

        Assert.Equal(0, result[0]);
        Assert.Equal(1, result[1]);
        Assert.Equal(0, result[2]);
        Assert.Equal(1, result[3]);
    }
}

public class DuplicateMergerTests
{
    private static EventOccurrence Copy(string account, string calendarId, AttendeeResponse? response, bool organizer = false, string uid = "meeting@google.com")
    {
        var e = Timed("m-" + account, "2026-11-06T10:00:00+01:00", "2026-11-06T11:00:00+01:00") with
        {
            Key = new EventKey(account, calendarId, "m"),
            ICalUid = uid,
            Organizer = new EventPerson("boss@example.com", null, organizer),
            Attendees = response is { } r ? [new Attendee($"{account}@example.com", null, r, true, organizer, false, false, null)] : [],
        };
        return new EventOccurrence(e, e.Start, e.End, null);
    }

    private static int Order(string account) => account switch { "personal" => 0, "work" => 1, _ => 2 };

    [Fact]
    public void Same_meeting_on_two_accounts_shows_once_with_both_copies()
    {
        var merged = DuplicateMerger.Merge([Copy("personal", "p", AttendeeResponse.NeedsAction), Copy("work", "w", AttendeeResponse.Accepted)], Order);

        var only = Assert.Single(merged);
        Assert.Equal(2, only.Copies.Count);
        Assert.Equal("work", only.Primary.Event.Key.AccountId); // the accepted copy wins over the unanswered one
    }

    [Fact]
    public void Organisers_copy_wins()
    {
        var merged = DuplicateMerger.Merge([Copy("personal", "p", AttendeeResponse.Accepted), Copy("work", "w", AttendeeResponse.Accepted, organizer: true)], Order);

        Assert.Equal("work", Assert.Single(merged).Primary.Event.Key.AccountId);
    }

    [Fact]
    public void Ties_go_to_the_earlier_account()
    {
        var merged = DuplicateMerger.Merge([Copy("work", "w", AttendeeResponse.Accepted), Copy("personal", "p", AttendeeResponse.Accepted)], Order);

        Assert.Equal("personal", Assert.Single(merged).Primary.Event.Key.AccountId);
    }

    [Fact]
    public void Different_meetings_and_events_without_a_uid_stay_apart()
    {
        var merged = DuplicateMerger.Merge(
        [
            Copy("personal", "p", null, uid: "a@google.com"),
            Copy("work", "w", null, uid: "b@google.com"),
            Copy("work", "w2", null, uid: ""),
            Copy("work", "w3", null, uid: ""),
        ], Order);

        Assert.Equal(4, merged.Count);
    }

    [Fact]
    public void Occurrences_of_one_series_stay_apart()
    {
        var series = Timed("s", "2026-11-02T09:00:00+01:00", "2026-11-02T09:15:00+01:00", Warsaw, "RRULE:FREQ=DAILY;COUNT=3") with
        {
            ICalUid = "series@google.com",
        };
        var occurrences = OccurrenceResolver.Resolve([series], Utc(2026, 11, 1), Utc(2026, 11, 10));

        Assert.Equal(3, DuplicateMerger.Merge(occurrences, Order).Count);
    }
}

public class HtmlTextTests
{
    [Theory]
    [InlineData("Agenda:<br>1. Intro<br/>2. Demo", "Agenda:\n1. Intro\n2. Demo")]
    [InlineData("<b>Bold</b> &amp; <i>italic</i>&nbsp;text", "Bold & italic text")]
    [InlineData("<ul><li>One</li><li>Two</li></ul>", "• One\n• Two")]
    [InlineData("Join <a href=\"https://zoom.us/j/1\">here</a>", "Join here (https://zoom.us/j/1)")]
    [InlineData("<a href=\"https://zoom.us/j/1\">https://zoom.us/j/1</a>", "https://zoom.us/j/1")]
    [InlineData("<p>One</p><p>Two</p>", "One\nTwo")]
    [InlineData("<table><tr><td>Segue</td><td>5 min</td></tr><tr><td>Scorecard</td><td>5 min</td></tr></table>", "Segue · 5 min\nScorecard · 5 min")]
    [InlineData("Plain text\n\n\n\nwith gaps", "Plain text\n\nwith gaps")]
    [InlineData(null, "")]
    public void Descriptions_become_plain_text(string? html, string expected)
    {
        Assert.Equal(expected, HtmlText.ToPlainText(html));
    }
}

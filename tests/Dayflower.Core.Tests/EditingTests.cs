// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using Dayflower.Core.Editing;
using Dayflower.Core.Model;
using Dayflower.Core.Recurrence;
using static Dayflower.Core.Tests.TestEvents;

namespace Dayflower.Core.Tests;

public class RecurrenceRuleTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    // Friday 6 November 2026, 10:00 in Warsaw.
    private static readonly EventTime Friday10 = EventTime.At(At("2026-11-06T10:00:00+01:00"), Warsaw);

    [Theory]
    [InlineData("RRULE:FREQ=DAILY", "Daily")]
    [InlineData("RRULE:FREQ=WEEKLY;BYDAY=MO,WE", "Weekly on Monday and Wednesday")]
    [InlineData("RRULE:FREQ=WEEKLY;BYDAY=MO,TU,WE,TH,FR", "Weekly on weekdays")]
    [InlineData("RRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=FR", "Every 2 weeks on Friday")]
    [InlineData("RRULE:FREQ=MONTHLY", "Monthly on day 6")]
    [InlineData("RRULE:FREQ=MONTHLY;BYDAY=1FR", "Monthly on the first Friday")]
    [InlineData("RRULE:FREQ=MONTHLY;BYDAY=-1FR", "Monthly on the last Friday")]
    [InlineData("RRULE:FREQ=YEARLY", "Annually on 6 November")]
    [InlineData("RRULE:FREQ=DAILY;COUNT=10", "Daily, 10 times")]
    [InlineData("RRULE:FREQ=WEEKLY;BYDAY=FR;UNTIL=20261218T225959Z", "Weekly on Friday, until 18 Dec")]
    [InlineData("RRULE:FREQ=WEEKLY;WKST=SU;BYDAY=FR", "Weekly on Friday")]
    public void Rules_are_read_and_described(string rrule, string description)
    {
        var rule = RecurrenceRule.Parse(rrule, Friday10);

        Assert.NotNull(rule);
        Assert.Equal(description, rule.Describe(Friday10, English));
    }

    [Theory]
    [InlineData("RRULE:FREQ=MONTHLY;BYSETPOS=-1;BYDAY=MO,TU,WE,TH,FR")]
    [InlineData("RRULE:FREQ=MONTHLY;BYMONTHDAY=15")]
    [InlineData("RRULE:FREQ=HOURLY")]
    [InlineData("RRULE:FREQ=MONTHLY;BYDAY=2MO")]
    public void Rules_it_cant_show_are_left_alone(string rrule)
    {
        Assert.Null(RecurrenceRule.Parse(rrule, Friday10));
    }

    [Fact]
    public void Built_rules_match_what_google_writes()
    {
        Assert.Equal("RRULE:FREQ=WEEKLY;BYDAY=FR", new RecurrenceRule { Frequency = RepeatFrequency.Weekly }.ToRRule(Friday10));
        Assert.Equal("RRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE",
            new RecurrenceRule { Frequency = RepeatFrequency.Weekly, Interval = 2, Days = [DayOfWeek.Wednesday, DayOfWeek.Monday] }.ToRRule(Friday10));
        Assert.Equal("RRULE:FREQ=MONTHLY;BYDAY=-1FR", new RecurrenceRule { Frequency = RepeatFrequency.Monthly, WeekOfMonth = -1 }.ToRRule(Friday10));
        Assert.Equal("RRULE:FREQ=DAILY;COUNT=5", new RecurrenceRule { Frequency = RepeatFrequency.Daily, Count = 5 }.ToRRule(Friday10));
    }

    [Fact]
    public void Until_is_the_end_of_that_day_in_utc_for_timed_events_and_a_date_for_all_day_ones()
    {
        var rule = new RecurrenceRule { Frequency = RepeatFrequency.Weekly, Until = new DateOnly(2026, 12, 18) };

        Assert.Equal("RRULE:FREQ=WEEKLY;BYDAY=FR;UNTIL=20261218T225959Z", rule.ToRRule(Friday10));
        Assert.Equal("RRULE:FREQ=WEEKLY;BYDAY=FR;UNTIL=20261218", rule.ToRRule(EventTime.AllDay(new DateOnly(2026, 11, 6))));
    }

    [Fact]
    public void A_built_rule_reads_back_the_same()
    {
        var rule = new RecurrenceRule { Frequency = RepeatFrequency.Monthly, Interval = 3, WeekOfMonth = 1, Until = new DateOnly(2027, 6, 30) };

        Assert.Equal(rule, RecurrenceRule.Parse(rule.ToRRule(Friday10), Friday10));
    }
}

public class SeriesSplitTests
{
    [Fact]
    public void Original_series_ends_just_before_the_split()
    {
        var lines = SeriesSplit.EndBefore(["RRULE:FREQ=WEEKLY;BYDAY=MO,WE;COUNT=20", "EXDATE;TZID=Europe/Warsaw:20261111T090000"],
                                          EventTime.At(At("2026-11-16T09:00:00+01:00"), Warsaw));

        Assert.Equal(["RRULE:FREQ=WEEKLY;BYDAY=MO,WE;UNTIL=20261116T075959Z", "EXDATE;TZID=Europe/Warsaw:20261111T090000"], lines);
    }

    [Fact]
    public void All_day_series_ends_the_day_before()
    {
        var lines = SeriesSplit.EndBefore(["RRULE:FREQ=DAILY;UNTIL=20270101"], EventTime.AllDay(new DateOnly(2026, 11, 20)));

        Assert.Equal(["RRULE:FREQ=DAILY;UNTIL=20261119"], lines);
    }

    [Fact]
    public void New_series_gets_the_remaining_count()
    {
        Assert.Equal(["RRULE:FREQ=DAILY;COUNT=7"], SeriesSplit.ContinueAfter(["RRULE:FREQ=DAILY;COUNT=10"], 3));
        Assert.Equal(["RRULE:FREQ=WEEKLY;BYDAY=FR"], SeriesSplit.ContinueAfter(["RRULE:FREQ=WEEKLY;BYDAY=FR"], 3));
    }

    [Fact]
    public void Occurrences_before_the_split_are_counted()
    {
        var series = Timed("daily", "2026-11-02T09:00:00+01:00", "2026-11-02T09:15:00+01:00", Warsaw, "RRULE:FREQ=DAILY;COUNT=10");

        Assert.Equal(4, SeriesSplit.OccurrencesBefore(series, EventTime.At(At("2026-11-06T09:00:00+01:00"), Warsaw)));
        Assert.Equal(0, SeriesSplit.OccurrencesBefore(series, series.Start));
    }

    [Fact]
    public void Split_series_together_have_the_same_occurrences()
    {
        var series = Timed("daily", "2026-11-02T09:00:00+01:00", "2026-11-02T09:15:00+01:00", Warsaw, "RRULE:FREQ=DAILY;COUNT=10");
        var split = EventTime.At(At("2026-11-06T09:00:00+01:00"), Warsaw);
        var before = series with { Recurrence = SeriesSplit.EndBefore(series.Recurrence, split) };
        var after = series with
        {
            Key = series.Key with { EventId = "daily_new" },
            Start = split,
            End = EventTime.At(At("2026-11-06T09:15:00+01:00"), Warsaw),
            Recurrence = SeriesSplit.ContinueAfter(series.Recurrence, SeriesSplit.OccurrencesBefore(series, split)),
        };

        var all = OccurrenceResolver.Resolve([before, after], Utc(2026, 11, 1), Utc(2026, 12, 1));

        Assert.Equal(Enumerable.Range(2, 10), all.Select(o => o.Start.DateTime!.Value.Day));
    }
}

public class SeriesShiftTests
{
    [Fact]
    public void Moving_an_occurrence_later_moves_the_series_by_the_same_local_time()
    {
        // Series started in October (summer time); the November occurrence moves from 09:00 to 11:30, 45 minutes long.
        var series = Timed("s", "2026-10-19T09:00:00+02:00", "2026-10-19T09:30:00+02:00", Warsaw, "RRULE:FREQ=WEEKLY;BYDAY=MO");

        var (start, end) = SeriesShift.Move(series,
            EventTime.At(At("2026-11-16T09:00:00+01:00"), Warsaw),
            EventTime.At(At("2026-11-16T11:30:00+01:00"), Warsaw),
            EventTime.At(At("2026-11-16T12:15:00+01:00"), Warsaw));

        Assert.Equal(At("2026-10-19T11:30:00+02:00"), start.DateTime);
        Assert.Equal(At("2026-10-19T12:15:00+02:00"), end.DateTime);
    }

    [Fact]
    public void Moving_an_occurrence_to_another_day_moves_the_series_start_by_days()
    {
        var series = Timed("s", "2026-11-02T09:00:00+01:00", "2026-11-02T09:30:00+01:00", Warsaw, "RRULE:FREQ=WEEKLY;BYDAY=MO");

        var (start, _) = SeriesShift.Move(series,
            EventTime.At(At("2026-11-16T09:00:00+01:00"), Warsaw),
            EventTime.At(At("2026-11-17T09:00:00+01:00"), Warsaw),
            EventTime.At(At("2026-11-17T09:30:00+01:00"), Warsaw));

        Assert.Equal(At("2026-11-03T09:00:00+01:00"), start.DateTime);
    }

    [Fact]
    public void All_day_series_moves_by_days()
    {
        var series = AllDay("a", new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3), "RRULE:FREQ=WEEKLY");

        var (start, end) = SeriesShift.Move(series, EventTime.AllDay(new DateOnly(2026, 11, 16)),
                                            EventTime.AllDay(new DateOnly(2026, 11, 18)), EventTime.AllDay(new DateOnly(2026, 11, 20)));

        Assert.Equal(new DateOnly(2026, 11, 4), start.Date);
        Assert.Equal(new DateOnly(2026, 11, 6), end.Date);
    }
}

public class EditPermissionTests
{
    private static CalendarEvent Invite(bool guestsCanModify = false, bool organizerIsSelf = false, string? organizerEmail = "boss@example.com") =>
        Timed("e", "2026-11-06T10:00:00+01:00", "2026-11-06T11:00:00+01:00") with
        {
            Organizer = new EventPerson(organizerEmail, null, organizerIsSelf),
            Attendees = organizerIsSelf ? [] : [new Attendee("me@example.com", null, AttendeeResponse.NeedsAction, true, false, false, false, null)],
            GuestsCanModify = guestsCanModify,
        };

    [Fact]
    public void Own_events_can_be_edited_with_write_access()
    {
        Assert.True(Invite(organizerIsSelf: true).CanEdit(CalendarAccess.Owner));
        Assert.False(Invite(organizerIsSelf: true).CanEdit(CalendarAccess.Reader));
    }

    [Fact]
    public void Someone_elses_invitation_can_only_be_answered_unless_guests_may_modify()
    {
        Assert.False(Invite().CanEdit(CalendarAccess.Owner));
        Assert.True(Invite().CanRespond);
        Assert.True(Invite(guestsCanModify: true).CanEdit(CalendarAccess.Owner));
    }

    [Fact]
    public void Events_organised_by_a_shared_calendar_can_be_edited_by_its_writers()
    {
        Assert.True(Invite(organizerEmail: "primary").CanEdit(CalendarAccess.Writer));
    }

    [Fact]
    public void Guests_who_may_modify_still_cant_delete()
    {
        Assert.False(Invite(guestsCanModify: true).CanDelete(CalendarAccess.Owner));
        Assert.True(Invite(organizerIsSelf: true).CanDelete(CalendarAccess.Owner));
        Assert.False(Invite(organizerIsSelf: true).CanDelete(CalendarAccess.Reader));
    }

    [Fact]
    public void Birthdays_and_gmail_events_are_left_to_google()
    {
        Assert.False((Invite(organizerIsSelf: true) with { Kind = EventKind.Birthday }).CanEdit(CalendarAccess.Owner));
        Assert.False((Invite(organizerIsSelf: true) with { Kind = EventKind.FromGmail }).CanDelete(CalendarAccess.Owner));
    }

    [Fact]
    public void Organiser_cant_respond_to_their_own_event()
    {
        Assert.False(Invite(organizerIsSelf: true).CanRespond);
    }
}

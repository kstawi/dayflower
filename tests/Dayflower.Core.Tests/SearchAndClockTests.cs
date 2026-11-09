// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Display;
using Dayflower.Core.Model;
using Dayflower.Core.Reminders;
using Dayflower.Core.Search;
using static Dayflower.Core.Tests.TestEvents;

namespace Dayflower.Core.Tests;

public class EventSearchTests
{
    private static readonly DateTimeOffset Now = At("2026-11-06T12:00:00+01:00");

    private static CalendarEvent Named(string id, string summary, string start, string end, params string[] recurrence) =>
        Timed(id, start, end, Warsaw, recurrence) with { Summary = summary };

    [Fact]
    public void Every_word_must_match_ignoring_case_and_accents()
    {
        var index = new EventSearchIndex(
        [
            Named("a", "Święta w Polsce", "2026-12-24T10:00:00+01:00", "2026-12-24T11:00:00+01:00"),
            Named("b", "Łódź office visit", "2026-11-20T10:00:00+01:00", "2026-11-20T11:00:00+01:00"),
            Named("c", "Polish lessons", "2026-11-10T10:00:00+01:00", "2026-11-10T11:00:00+01:00"),
        ]);

        Assert.Equal(["a"], index.Find("swieta polsce", Now).Select(o => o.Event.Key.EventId));
        Assert.Equal(["b"], index.Find("LODZ", Now).Select(o => o.Event.Key.EventId));
        Assert.Empty(index.Find("swieta lessons", Now));
        Assert.Empty(index.Find("   ", Now));
    }

    [Fact]
    public void Location_description_and_guests_are_searched()
    {
        var e = Named("a", "Sync", "2026-11-10T10:00:00+01:00", "2026-11-10T11:00:00+01:00") with
        {
            Location = "Room Orion",
            Description = "<p>Agenda: <b>budget</b></p>",
            Attendees = [new Attendee("anna.nowak@example.com", "Anna Nowak", AttendeeResponse.Accepted, false, false, false, false, null)],
        };
        var index = new EventSearchIndex([e]);

        Assert.Single(index.Find("orion", Now));
        Assert.Single(index.Find("budget", Now));
        Assert.Single(index.Find("nowak", Now));
        Assert.Single(index.Find("anna.nowak@example", Now));
        Assert.Empty(index.Find("agenda budget orion zebra", Now));
    }

    [Fact]
    public void Upcoming_results_come_first_soonest_first_then_past_ones_latest_first()
    {
        var index = new EventSearchIndex(
        [
            Named("old", "Review", "2026-10-01T10:00:00+02:00", "2026-10-01T11:00:00+02:00"),
            Named("later", "Review", "2026-12-01T10:00:00+01:00", "2026-12-01T11:00:00+01:00"),
            Named("recent", "Review", "2026-11-02T10:00:00+01:00", "2026-11-02T11:00:00+01:00"),
            Named("soon", "Review", "2026-11-09T10:00:00+01:00", "2026-11-09T11:00:00+01:00"),
            Named("now", "Review", "2026-11-06T11:30:00+01:00", "2026-11-06T12:30:00+01:00"),
        ]);

        Assert.Equal(["now", "soon", "later", "recent", "old"], index.Find("review", Now).Select(o => o.Event.Key.EventId));
    }

    [Fact]
    public void A_repeating_event_shows_once_at_its_next_occurrence()
    {
        var weekly = Named("weekly", "Standup", "2026-10-01T09:00:00+02:00", "2026-10-01T09:15:00+02:00", "RRULE:FREQ=WEEKLY");
        var moved = Override(weekly, "weekly_x", "2026-11-12T09:00:00+01:00", "2026-11-12T14:00:00+01:00", "2026-11-12T14:15:00+01:00");
        var index = new EventSearchIndex([weekly, moved]);

        Assert.Equal(At("2026-11-12T09:00:00+01:00"), Assert.Single(new EventSearchIndex([weekly]).Find("standup", Now)).Start.EarliestUtc);

        // The next occurrence was moved, so that's the one shown; the changed occurrence isn't listed twice.
        var result = Assert.Single(index.Find("standup", Now));
        Assert.Same(moved, result.Event);
        Assert.Equal(At("2026-11-12T14:00:00+01:00"), result.Start.EarliestUtc);
        Assert.Same(moved, Assert.Single(index.Find("changed", Now)).Event);
    }

    [Fact]
    public void A_repeating_event_that_ended_shows_its_last_occurrence()
    {
        var ended = Named("ended", "Course", "2026-09-01T18:00:00+02:00", "2026-09-01T19:00:00+02:00", "RRULE:FREQ=WEEKLY;COUNT=4");

        var result = Assert.Single(new EventSearchIndex([ended]).Find("course", Now));

        Assert.Equal(At("2026-09-22T18:00:00+02:00"), result.Start.EarliestUtc);
    }

    [Fact]
    public void Cancelled_events_are_not_found()
    {
        var cancelled = Named("x", "Offsite", "2026-11-10T10:00:00+01:00", "2026-11-10T11:00:00+01:00") with { Status = EventStatus.Cancelled };

        Assert.Empty(new EventSearchIndex([cancelled]).Find("offsite", Now));
    }
}

public class WorldClockTests
{
    private static readonly TimeZoneInfo Warsaw = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    private static readonly TimeZoneInfo Tokyo = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
    private static readonly TimeZoneInfo Kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

    [Fact]
    public void Reads_the_time_difference_and_day()
    {
        var now = At("2026-11-06T03:30:00+01:00");

        var ny = WorldClock.Read(now, NewYork, Warsaw);
        Assert.Equal(new DateTime(2026, 11, 5, 21, 30, 0), ny.Time);
        Assert.Equal(TimeSpan.FromHours(-6), ny.Difference);
        Assert.Equal(-1, ny.DayOffset);

        var tokyo = WorldClock.Read(now, Tokyo, Warsaw);
        Assert.Equal(TimeSpan.FromHours(8), tokyo.Difference);
        Assert.Equal(0, tokyo.DayOffset);
    }

    [Fact]
    public void Daylight_saving_changes_the_difference()
    {
        // The US moves its clocks a few weeks before Europe in spring.
        Assert.Equal(TimeSpan.FromHours(-5), WorldClock.Read(At("2026-03-20T12:00:00+01:00"), NewYork, Warsaw).Difference);
        Assert.Equal(TimeSpan.FromHours(-6), WorldClock.Read(At("2026-04-20T12:00:00+02:00"), NewYork, Warsaw).Difference);
    }

    [Fact]
    public void Differences_are_written_compactly()
    {
        Assert.Equal("−6h", WorldClock.FormatDifference(TimeSpan.FromHours(-6)));
        Assert.Equal("+8h", WorldClock.FormatDifference(TimeSpan.FromHours(8)));
        Assert.Equal("+4:30", WorldClock.FormatDifference(WorldClock.Read(At("2026-11-06T12:00:00+01:00"), Kolkata, Warsaw).Difference));
        Assert.Equal("Same time", WorldClock.FormatDifference(TimeSpan.Zero));
        Assert.Equal("tomorrow", WorldClock.FormatDay(1));
        Assert.Null(WorldClock.FormatDay(0));
    }

    [Fact]
    public void Every_city_has_a_time_zone_windows_knows()
    {
        foreach (var city in Cities.All)
        {
            Assert.NotNull(TimeZoneInfo.FindSystemTimeZoneById(city.ZoneId));
        }
    }

    [Fact]
    public void Cities_are_found_by_the_start_of_their_name_or_country()
    {
        Assert.Equal("New York", Cities.Find("new y")[0].Name);
        Assert.Contains(Cities.Find("san"), c => c.Name == "San Francisco");
        Assert.Contains(Cities.Find("krakow"), c => c.Name == "Kraków");
        Assert.Contains(Cities.Find("japan"), c => c.Name == "Tokyo");
        Assert.DoesNotContain(Cities.Find("york"), c => c.Name == "Tokyo");
    }
}

public class InvitationWatchTests
{
    private static readonly DateTimeOffset Now = At("2026-11-06T12:00:00+01:00");

    private static CalendarEvent Invite(string id, string start, AttendeeResponse response = AttendeeResponse.NeedsAction, string account = "google:1",
                                        string? uid = null, params string[] recurrence) =>
        Timed(id, start, At(start).AddHours(1).ToString("o"), Warsaw, recurrence) with
        {
            Key = new EventKey(account, "me@example.com", id),
            ICalUid = uid ?? id + "@google.com",
            Organizer = new EventPerson("boss@example.com", null, false),
            Attendees = [new Attendee("me@example.com", null, response, true, false, false, false, null)],
        };

    [Fact]
    public void Announces_new_unanswered_invitations_once()
    {
        var known = new HashSet<string>();
        var seeded = new HashSet<string> { "google:1" };
        var invite = Invite("a", "2026-11-10T10:00:00+01:00");

        var first = InvitationWatch.FindNew([invite], Now, known, seeded, ["google:1"]);
        var second = InvitationWatch.FindNew([invite], Now, known, seeded, ["google:1"]);

        Assert.Single(first);
        Assert.Empty(second);
    }

    [Fact]
    public void Answered_past_and_own_events_are_not_announced()
    {
        var seeded = new HashSet<string> { "google:1" };
        var events = new[]
        {
            Invite("answered", "2026-11-10T10:00:00+01:00", AttendeeResponse.Accepted),
            Invite("past", "2026-11-01T10:00:00+01:00"),
            Timed("own", "2026-11-10T10:00:00+01:00", "2026-11-10T11:00:00+01:00"),
        };

        Assert.Empty(InvitationWatch.FindNew(events, Now, new HashSet<string>(), seeded, ["google:1"]));
    }

    [Fact]
    public void A_repeating_invitation_with_occurrences_ahead_is_announced()
    {
        var weekly = Invite("weekly", "2026-10-01T10:00:00+02:00", recurrence: "RRULE:FREQ=WEEKLY");

        Assert.Single(InvitationWatch.FindNew([weekly], Now, new HashSet<string>(), new HashSet<string> { "google:1" }, ["google:1"]));
    }

    [Fact]
    public void A_newly_added_account_is_remembered_without_announcing()
    {
        var known = new HashSet<string>();
        var seeded = new HashSet<string> { "google:1" };
        var existing = Invite("old", "2026-11-10T10:00:00+01:00", account: "google:2");

        Assert.Empty(InvitationWatch.FindNew([existing], Now, known, seeded, ["google:1", "google:2"]));
        var later = Invite("new", "2026-11-11T10:00:00+01:00", account: "google:2");
        Assert.Equal(["new"], InvitationWatch.FindNew([existing, later], Now, known, seeded, ["google:1", "google:2"]).Select(e => e.Key.EventId));
    }

    [Fact]
    public void A_meeting_on_two_accounts_is_announced_once()
    {
        var copies = new[]
        {
            Invite("a", "2026-11-10T10:00:00+01:00", uid: "same@google.com"),
            Invite("b", "2026-11-10T10:00:00+01:00", account: "google:2", uid: "same@google.com"),
        };

        Assert.Single(InvitationWatch.FindNew(copies, Now, new HashSet<string>(), new HashSet<string> { "google:1", "google:2" }, ["google:1", "google:2"]));
    }

    [Fact]
    public void An_invitation_answered_and_then_reset_is_announced_again()
    {
        var known = new HashSet<string>();
        var seeded = new HashSet<string> { "google:1" };
        var open = Invite("a", "2026-11-10T10:00:00+01:00");

        InvitationWatch.FindNew([open], Now, known, seeded, ["google:1"]);
        InvitationWatch.FindNew([Invite("a", "2026-11-10T10:00:00+01:00", AttendeeResponse.Accepted)], Now, known, seeded, ["google:1"]);

        Assert.Single(InvitationWatch.FindNew([open], Now, known, seeded, ["google:1"]));
    }
}

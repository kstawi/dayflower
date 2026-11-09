// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Display;
using Dayflower.Core.Model;
using Dayflower.Core.Recurrence;
using Dayflower.Core.Reminders;
using static Dayflower.Core.Tests.TestEvents;

namespace Dayflower.Core.Tests;

public class ReminderPlannerTests
{
    private static readonly TimeZoneInfo WarsawZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");

    private static readonly IReadOnlyList<Reminder> TenMinutes = [new Reminder(ReminderMethod.Popup, 10)];

    private static MergedOccurrence Single(CalendarEvent e) => One(new EventOccurrence(e, e.Start, e.End, e.OriginalStart));

    private static MergedOccurrence One(params EventOccurrence[] copies) => new(copies[0], copies);

    private static CalendarEvent Meeting(string id = "m", ReminderSettings? reminders = null, AttendeeResponse? response = null, string account = "google:1") =>
        Timed(id, "2026-11-06T10:00:00+01:00", "2026-11-06T10:30:00+01:00") with
        {
            Key = new EventKey(account, "primary", id),
            ICalUid = id + "@google.com",
            Reminders = reminders ?? ReminderSettings.Default,
            Attendees = response is { } r ? [new Attendee("me@example.com", null, r, true, false, false, false, null)] : [],
        };

    private static IReadOnlyList<PlannedReminder> Plan(IEnumerable<MergedOccurrence> occurrences, bool startingNow = false, bool meetings = true) =>
        ReminderPlanner.Plan(occurrences, (_, _) => TenMinutes, _ => meetings, startingNow, WarsawZone);

    [Fact]
    public void Event_using_defaults_gets_its_calendars_reminders()
    {
        var plan = Plan([Single(Meeting())]);

        var only = Assert.Single(plan);
        Assert.Equal(At("2026-11-06T08:50:00Z"), only.FireAt);
        Assert.Equal(At("2026-11-06T09:30:00Z"), only.EventEnd);
        Assert.Equal(ReminderKind.Reminder, only.Kind);
    }

    [Fact]
    public void Event_with_its_own_reminders_uses_only_the_popup_ones()
    {
        var reminders = new ReminderSettings(false, [new Reminder(ReminderMethod.Popup, 30), new Reminder(ReminderMethod.Email, 60), new Reminder(ReminderMethod.Popup, 5)]);

        var plan = Plan([Single(Meeting(reminders: reminders))]);

        Assert.Equal([30, 5], plan.Select(r => r.MinutesBefore));
    }

    [Fact]
    public void Event_with_reminders_turned_off_gets_none()
    {
        Assert.Empty(Plan([Single(Meeting(reminders: new ReminderSettings(false, [])))]));
    }

    [Fact]
    public void Declined_events_dont_remind()
    {
        Assert.Empty(Plan([Single(Meeting(response: AttendeeResponse.Declined))], startingNow: true));
    }

    [Fact]
    public void All_day_reminder_counts_from_local_midnight()
    {
        var e = AllDay("trip", new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 10)) with
        {
            Reminders = new ReminderSettings(false, [new Reminder(ReminderMethod.Popup, 15 * 60)]),
        };

        var only = Assert.Single(Plan([Single(e)]));

        Assert.Equal(At("2026-11-08T09:00:00+01:00"), only.FireAt); // 9:00 the day before, in Warsaw
    }

    [Fact]
    public void All_day_event_using_defaults_gets_none_because_google_doesnt_share_them()
    {
        var e = AllDay("holiday", new DateOnly(2026, 11, 11), new DateOnly(2026, 11, 12));

        Assert.Empty(Plan([Single(e)]));
    }

    [Fact]
    public void Meeting_on_two_accounts_reminds_once_per_distinct_time()
    {
        var personal = Meeting(account: "google:p", reminders: new ReminderSettings(false, [new Reminder(ReminderMethod.Popup, 10)]));
        var work = Meeting(account: "google:w", reminders: new ReminderSettings(false, [new Reminder(ReminderMethod.Popup, 10), new Reminder(ReminderMethod.Popup, 1)]));
        var merged = One(new EventOccurrence(work, work.Start, work.End, null), new EventOccurrence(personal, personal.Start, personal.End, null));

        var plan = Plan([merged]);

        Assert.Equal([10, 1], plan.Select(r => r.MinutesBefore));
        Assert.Single(plan.Select(r => r.OccurrenceKey).Distinct());
    }

    [Fact]
    public void Meetings_get_a_starting_now_alert_unless_a_reminder_is_already_at_the_start()
    {
        var plan = Plan([Single(Meeting())], startingNow: true);
        var atStart = Plan([Single(Meeting(reminders: new ReminderSettings(false, [new Reminder(ReminderMethod.Popup, 0)])))], startingNow: true);
        var notAMeeting = Plan([Single(Meeting())], startingNow: true, meetings: false);

        Assert.Equal([ReminderKind.Reminder, ReminderKind.StartingNow], plan.Select(r => r.Kind));
        Assert.Equal(At("2026-11-06T09:00:00Z"), plan[1].FireAt);
        Assert.Equal([ReminderKind.Reminder], atStart.Select(r => r.Kind));
        Assert.Equal([ReminderKind.Reminder], notAMeeting.Select(r => r.Kind));
    }

    [Fact]
    public void Each_occurrence_of_a_series_has_its_own_reminders()
    {
        var series = Timed("daily", "2026-11-02T09:00:00+01:00", "2026-11-02T09:15:00+01:00", Warsaw, "RRULE:FREQ=DAILY;COUNT=3");
        var occurrences = OccurrenceResolver.Resolve([series], Utc(2026, 11, 1), Utc(2026, 11, 10)).Select(o => One(o));

        var plan = Plan(occurrences);

        Assert.Equal(3, plan.Select(r => r.OccurrenceKey).Distinct().Count());
        Assert.Equal(At("2026-11-03T07:50:00Z"), plan[1].FireAt);
    }

    [Fact]
    public void Due_skips_what_was_shown_and_events_that_have_ended()
    {
        var plan = Plan([Single(Meeting("a")), Single(Meeting("b"))]);
        var shown = new HashSet<string> { plan.Single(r => r.Key.Contains("|a|", StringComparison.Ordinal)).Key };

        var dueDuring = ReminderPlanner.Due(plan, shown, At("2026-11-06T09:05:00Z"));
        var dueAfter = ReminderPlanner.Due(plan, new HashSet<string>(), At("2026-11-06T09:31:00Z"));
        var dueBefore = ReminderPlanner.Due(plan, new HashSet<string>(), At("2026-11-06T08:49:00Z"));

        Assert.Contains("|b|", Assert.Single(dueDuring).Key);
        Assert.Empty(dueAfter);
        Assert.Empty(dueBefore);
    }

    [Fact]
    public void Missed_reminders_catch_up_with_only_the_latest_per_occurrence()
    {
        // Asleep from 8:00 to 9:05 UTC: the 30- and 10-minute reminders and the start alert were all missed.
        var reminders = new ReminderSettings(false, [new Reminder(ReminderMethod.Popup, 30), new Reminder(ReminderMethod.Popup, 10)]);
        var plan = Plan([Single(Meeting(reminders: reminders))], startingNow: true);

        var due = ReminderPlanner.Due(plan, new HashSet<string>(), At("2026-11-06T09:05:00Z"));

        Assert.Equal(ReminderKind.StartingNow, Assert.Single(due).Kind);
    }
}

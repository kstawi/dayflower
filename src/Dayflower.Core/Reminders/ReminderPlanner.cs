// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using Dayflower.Core.Display;
using Dayflower.Core.Model;

namespace Dayflower.Core.Reminders;

public enum ReminderKind
{
    /// <summary>One of the event's reminders, e.g. 10 minutes before.</summary>
    Reminder,

    /// <summary>The extra alert when a meeting starts.</summary>
    StartingNow,
}

/// <summary>A notification due at <see cref="FireAt"/> for one occurrence of an event.</summary>
/// <param name="Key">Unique per occurrence and reminder, so each is shown once.</param>
/// <param name="OccurrenceKey">The same for every reminder of one occurrence.</param>
public sealed record PlannedReminder(
    string Key, string OccurrenceKey, MergedOccurrence Occurrence, DateTimeOffset FireAt, int MinutesBefore, ReminderKind Kind)
{
    /// <summary>When the occurrence ends; reminders aren't shown after that.</summary>
    public DateTimeOffset EventEnd { get; init; }
}

/// <summary>
/// Works out when to notify, the way Google Calendar does: each event's own pop-up reminders, or its calendar's
/// default ones when the event uses defaults. Email reminders are Google's job. Declined events don't remind.
/// </summary>
public static class ReminderPlanner
{
    /// <param name="calendarDefaults">A calendar's default reminders, by account and calendar ID.</param>
    /// <param name="isMeeting">Whether an occurrence has a meeting to join (for the "starting now" alert).</param>
    /// <param name="startingNowAlert">Whether meetings also get an alert at their start time.</param>
    public static IReadOnlyList<PlannedReminder> Plan(
        IEnumerable<MergedOccurrence> occurrences,
        Func<string, string, IReadOnlyList<Reminder>> calendarDefaults,
        Func<MergedOccurrence, bool> isMeeting,
        bool startingNowAlert,
        TimeZoneInfo zone)
    {
        var result = new List<PlannedReminder>();
        foreach (var occurrence in occurrences)
        {
            var primary = occurrence.Primary;
            if (primary.Event.Status == EventStatus.Cancelled || primary.Event.Self?.Response == AttendeeResponse.Declined)
            {
                continue;
            }

            var start = StartUtc(primary.Start, zone);
            var end = primary.End.IsAllDay ? StartUtc(primary.End, zone) : primary.End.DateTime!.Value;
            string occurrenceKey = OccurrenceKey(occurrence);

            // Every copy's reminders count (a meeting on two accounts may have different ones), once each.
            var minutes = occurrence.Copies
                .Where(c => c.Event.Self?.Response != AttendeeResponse.Declined)
                .SelectMany(c => PopupMinutes(c.Event, calendarDefaults))
                .Distinct()
                .ToList();

            foreach (int m in minutes)
            {
                result.Add(new PlannedReminder($"{occurrenceKey}@{m}", occurrenceKey, occurrence, start.AddMinutes(-m), m, ReminderKind.Reminder)
                {
                    EventEnd = end,
                });
            }

            if (startingNowAlert && !primary.Start.IsAllDay && isMeeting(occurrence) && !minutes.Contains(0))
            {
                result.Add(new PlannedReminder($"{occurrenceKey}@start", occurrenceKey, occurrence, start, 0, ReminderKind.StartingNow)
                {
                    EventEnd = end,
                });
            }
        }

        return result.OrderBy(r => r.FireAt).ToList();
    }

    /// <summary>
    /// The reminders to show now: due, not shown yet, and for an event that hasn't ended. Reminders missed while the
    /// PC was asleep or the app wasn't running are caught up, but only the latest one per occurrence is shown.
    /// </summary>
    public static IReadOnlyList<PlannedReminder> Due(IEnumerable<PlannedReminder> plan, IReadOnlySet<string> shown, DateTimeOffset now) =>
        plan.Where(r => r.FireAt <= now && r.EventEnd > now && !shown.Contains(r.Key))
            .GroupBy(r => r.OccurrenceKey)
            .Select(g => g.MaxBy(r => r.FireAt)!)
            .OrderBy(r => r.FireAt)
            .ToList();

    /// <summary>Identifies one occurrence across syncs and restarts.</summary>
    public static string OccurrenceKey(MergedOccurrence occurrence)
    {
        var o = occurrence.Primary;
        string id = o.Event.RecurringEventId ?? o.Event.Key.EventId;
        return string.Create(CultureInfo.InvariantCulture,
            $"{o.Event.Key.AccountId}|{o.Event.Key.CalendarId}|{id}|{(o.OriginalStart ?? o.Start).OccurrenceKey}");
    }

    private static IEnumerable<int> PopupMinutes(CalendarEvent e, Func<string, string, IReadOnlyList<Reminder>> calendarDefaults)
    {
        IEnumerable<Reminder> reminders;
        if (e.Reminders.UseDefault)
        {
            // Google keeps separate defaults for all-day events, and the API doesn't expose them.
            reminders = e.Start.IsAllDay ? [] : calendarDefaults(e.Key.AccountId, e.Key.CalendarId);
        }
        else
        {
            reminders = e.Reminders.Overrides;
        }

        return reminders.Where(r => r.Method == ReminderMethod.Popup && r.MinutesBefore >= 0).Select(r => r.MinutesBefore);
    }

    /// <summary>An all-day event starts at local midnight; a reminder "15 hours before" is 9:00 the day before.</summary>
    private static DateTimeOffset StartUtc(EventTime time, TimeZoneInfo zone) =>
        time.IsAllDay ? LocalDays.StartUtc(time.Date!.Value, zone) : time.DateTime!.Value;
}

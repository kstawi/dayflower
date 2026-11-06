// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Model;
using Dayflower.Core.Recurrence;

namespace Dayflower.Core.Reminders;

/// <summary>
/// Which invitations to announce after a sync: ones waiting for an answer that weren't waiting before. Invitations
/// already there when an account is first seen are only remembered, so adding an account doesn't bring a flood.
/// </summary>
public static class InvitationWatch
{
    /// <summary>A stable key for an event's copy in one calendar.</summary>
    public static string Key(CalendarEvent e) => $"{e.Key.AccountId}|{e.Key.CalendarId}|{e.Key.EventId}";

    /// <summary>
    /// Whether <paramref name="e"/> is an invitation the user hasn't answered, for something still to come: a one-off
    /// event that hasn't ended, or a repeating one with occurrences ahead. Changed occurrences of a series aren't
    /// announced on their own.
    /// </summary>
    public static bool IsOpen(CalendarEvent e, DateTimeOffset now) =>
        e.CanRespond
        && e.Self!.Response == AttendeeResponse.NeedsAction
        && e.Kind == EventKind.Default
        && !e.IsOccurrenceOverride
        && (e.IsSeries ? RecurrenceExpander.Expand(e, now, now.AddYears(1)).Count > 0 : e.End.LatestUtc > now);

    /// <summary>
    /// The new invitations to announce, once per meeting (copies on several accounts share an iCalendar UID).
    /// </summary>
    /// <param name="events">Events of the user's own (primary) calendars.</param>
    /// <param name="known">Invitations already seen; updated to the ones open now.</param>
    /// <param name="seededAccounts">
    /// Accounts whose invitations have been looked at before; <paramref name="syncedAccounts"/> are added to it.
    /// </param>
    /// <param name="syncedAccounts">Accounts synced at least once, so their invitations are all in the cache.</param>
    public static IReadOnlyList<CalendarEvent> FindNew(
        IEnumerable<CalendarEvent> events, DateTimeOffset now, ISet<string> known, ISet<string> seededAccounts,
        IReadOnlyCollection<string> syncedAccounts)
    {
        var open = events.Where(e => IsOpen(e, now)).ToList();
        var announce = open
            .Where(e => seededAccounts.Contains(e.Key.AccountId) && !known.Contains(Key(e)))
            .DistinctBy(e => e.ICalUid ?? Key(e))
            .OrderBy(e => e.Start.EarliestUtc)
            .ToList();

        // An answered invitation is forgotten, so it's announced again if the organiser asks again.
        known.Clear();
        known.UnionWith(open.Select(Key));
        seededAccounts.Clear();
        seededAccounts.UnionWith(syncedAccounts);
        return announce;
    }
}

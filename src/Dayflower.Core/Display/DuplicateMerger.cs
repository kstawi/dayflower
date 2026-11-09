// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Model;
using Dayflower.Core.Recurrence;

namespace Dayflower.Core.Display;

/// <summary>
/// One meeting as shown: the copy whose details are used (<see cref="Primary"/>) and every copy of it on the
/// user's accounts and calendars (<see cref="Copies"/>, starting with the primary).
/// </summary>
public sealed record MergedOccurrence(EventOccurrence Primary, IReadOnlyList<EventOccurrence> Copies);

/// <summary>
/// Shows a meeting that's on several of the user's calendars once. Copies are the same meeting when they share the
/// iCalendar UID and the occurrence (for a repeating meeting, the originally scheduled start).
/// </summary>
public static class DuplicateMerger
{
    /// <param name="accountOrder">Position of an account in the user's list; earlier accounts win ties.</param>
    public static IReadOnlyList<MergedOccurrence> Merge(IEnumerable<EventOccurrence> occurrences, Func<string, int> accountOrder)
    {
        var result = new List<MergedOccurrence>();
        foreach (var group in occurrences.GroupBy(KeyOf))
        {
            var copies = group
                .OrderBy(Rank)
                .ThenBy(o => accountOrder(o.Event.Key.AccountId))
                .ToList();
            result.Add(new MergedOccurrence(copies[0], copies));
        }

        return result;
    }

    private static object KeyOf(EventOccurrence o) =>
        o.Event.ICalUid is { Length: > 0 } uid
            ? (uid, (o.OriginalStart ?? o.Start).OccurrenceKey)
            : o.Event.Key with { EventId = o.Event.Key.EventId + "@" + o.Start.OccurrenceKey };

    /// <summary>The copy to show: the organiser's, then one that's accepted, then tentative, then unanswered.</summary>
    private static int Rank(EventOccurrence o)
    {
        var e = o.Event;
        if (e.Organizer?.IsSelf == true || e.Self?.IsOrganizer == true)
        {
            return 0;
        }

        return e.Self?.Response switch
        {
            null => 1,
            AttendeeResponse.Accepted => 1,
            AttendeeResponse.Tentative => 2,
            AttendeeResponse.NeedsAction => 3,
            _ => 4,
        };
    }
}

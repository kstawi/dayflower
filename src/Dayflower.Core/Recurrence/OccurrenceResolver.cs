// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Model;

namespace Dayflower.Core.Recurrence;

/// <summary>
/// An event as it actually happens in a date range: a one-off event, an unchanged occurrence of a series, or a
/// changed occurrence.
/// </summary>
/// <param name="Event">The event the details come from: the one-off event, the series, or the changed occurrence.</param>
/// <param name="OriginalStart">For occurrences of a series, when the occurrence was scheduled by the rule.</param>
public sealed record EventOccurrence(CalendarEvent Event, EventTime Start, EventTime End, EventTime? OriginalStart);

/// <summary>Turns cached events (series, changed occurrences and one-off events) into what happens in a range.</summary>
public static class OccurrenceResolver
{
    /// <summary>
    /// Everything that happens between <paramref name="from"/> and <paramref name="to"/>, in start order.
    /// </summary>
    /// <param name="events">
    /// The cached events for the range, including every changed or cancelled occurrence whose original start is
    /// in the range (even if it was moved out of it).
    /// </param>
    public static IReadOnlyList<EventOccurrence> Resolve(IEnumerable<CalendarEvent> events, DateTimeOffset from, DateTimeOffset to)
    {
        var all = events.ToList();

        var overrides = all
            .Where(e => e.IsOccurrenceOverride && e.OriginalStart is not null)
            .GroupBy(e => (e.Key.AccountId, e.Key.CalendarId, SeriesId: e.RecurringEventId!))
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(e => e.OriginalStart!.OccurrenceKey).ToDictionary(x => x.Key, x => x.First()));

        var result = new List<EventOccurrence>();

        foreach (var series in all.Where(e => e.IsSeries && e.Status != EventStatus.Cancelled))
        {
            overrides.TryGetValue((series.Key.AccountId, series.Key.CalendarId, series.Key.EventId), out var changed);
            foreach (var occurrence in RecurrenceExpander.Expand(series, from, to))
            {
                if (changed is not null && changed.ContainsKey(occurrence.Start.OccurrenceKey))
                {
                    continue; // replaced or cancelled; a changed occurrence is added below on its own dates
                }

                result.Add(new EventOccurrence(series, occurrence.Start, occurrence.End, occurrence.Start));
            }
        }

        foreach (var e in all.Where(e => !e.IsSeries && e.Status != EventStatus.Cancelled))
        {
            if (RecurrenceExpander.Overlaps(e.Start, e.End, from, to))
            {
                result.Add(new EventOccurrence(e, e.Start, e.End, e.OriginalStart));
            }
        }

        return result
            .OrderBy(o => o.Start.EarliestUtc)
            .ThenBy(o => o.End.LatestUtc)
            .ToList();
    }
}

// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using System.Text;
using Dayflower.Core.Display;
using Dayflower.Core.Model;
using Dayflower.Core.Recurrence;

namespace Dayflower.Core.Search;

/// <summary>
/// Search over the cached events: every word of the query has to appear in the title, location, description, guests
/// or organiser, ignoring case and accents ("swieta" finds "Święta"). Works offline.
/// </summary>
public sealed class EventSearchIndex
{
    private static readonly TimeSpan NearWindow = TimeSpan.FromDays(60);
    private static readonly TimeSpan FarWindow = TimeSpan.FromDays(730);

    private readonly List<(CalendarEvent Event, string Text)> _entries;
    private readonly ILookup<(string, string, string), CalendarEvent> _overrides;

    public EventSearchIndex(IEnumerable<CalendarEvent> events)
    {
        var all = events.ToList();
        _entries = all.Where(e => e.Status != EventStatus.Cancelled).Select(e => (e, Fold(Text(e)))).ToList();
        _overrides = all.Where(e => e.IsOccurrenceOverride)
            .ToLookup(e => (e.Key.AccountId, e.Key.CalendarId, e.RecurringEventId!));
    }

    public int Count => _entries.Count;

    /// <summary>
    /// What to show for <paramref name="query"/>: one-off events and changed occurrences as they are, and for a
    /// repeating event its next occurrence (or its last, once it has ended). Upcoming ones come first, soonest first,
    /// then past ones, most recent first.
    /// </summary>
    public IReadOnlyList<EventOccurrence> Find(string query, DateTimeOffset now, int limit = 200)
    {
        var words = Words(query);
        if (words.Count == 0)
        {
            return [];
        }

        var matches = _entries.Where(e => words.All(w => e.Text.Contains(w, StringComparison.Ordinal))).Select(e => e.Event).ToList();
        var matchedSeries = matches.Where(e => e.IsSeries).Select(e => (e.Key.AccountId, e.Key.CalendarId, e.Key.EventId)).ToHashSet();

        var results = new List<EventOccurrence>();
        foreach (var e in matches)
        {
            if (e.IsSeries)
            {
                if (Representative(e, now) is { } occurrence)
                {
                    results.Add(occurrence);
                }
            }
            else if (!(e.IsOccurrenceOverride && matchedSeries.Contains((e.Key.AccountId, e.Key.CalendarId, e.RecurringEventId!))))
            {
                // A changed occurrence is shown on its own only when its series didn't match too.
                results.Add(new EventOccurrence(e, e.Start, e.End, e.OriginalStart));
            }
        }

        var upcoming = results.Where(o => o.End.LatestUtc > now).OrderBy(o => o.Start.EarliestUtc);
        var past = results.Where(o => o.End.LatestUtc <= now).OrderByDescending(o => o.Start.EarliestUtc);
        return upcoming.Concat(past).Take(limit).ToList();
    }

    /// <summary>The query's words, folded like the indexed text.</summary>
    public static IReadOnlyList<string> Words(string query) =>
        Fold(query).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();

    /// <summary>Lower case without accents, so "Łódź" and "lodz" compare equal.</summary>
    public static string Fold(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            // Letters with a stroke have no separate accent to drop.
            folded.Append(char.ToLowerInvariant(c) switch
            {
                'ł' => 'l',
                'ø' => 'o',
                'đ' => 'd',
                'ı' => 'i',
                var other => other,
            });
        }

        return folded.ToString().Replace("ß", "ss", StringComparison.Ordinal);
    }

    private static string Text(CalendarEvent e)
    {
        var text = new StringBuilder();
        text.Append(e.Summary).Append('\n').Append(e.Location).Append('\n').Append(HtmlText.ToPlainText(e.Description));
        foreach (var a in e.Attendees)
        {
            text.Append('\n').Append(a.DisplayName).Append(' ').Append(a.Email);
        }

        if (e.Organizer is { } organizer)
        {
            text.Append('\n').Append(organizer.DisplayName).Append(' ').Append(organizer.Email);
        }

        return text.ToString();
    }

    /// <summary>A repeating event's next occurrence from <paramref name="now"/>, or its last one if it has ended.</summary>
    private EventOccurrence? Representative(CalendarEvent series, DateTimeOffset now)
    {
        CalendarEvent[] related = [series, .. _overrides[(series.Key.AccountId, series.Key.CalendarId, series.Key.EventId)]];
        bool OfSeries(EventOccurrence o) => o.Event == series || o.Event.RecurringEventId == series.Key.EventId;

        foreach (var window in new[] { NearWindow, FarWindow })
        {
            var next = OccurrenceResolver.Resolve(related, now, now + window).FirstOrDefault(o => OfSeries(o) && o.End.LatestUtc > now);
            if (next is not null)
            {
                return next;
            }
        }

        foreach (var window in new[] { NearWindow, FarWindow })
        {
            var last = OccurrenceResolver.Resolve(related, now - window, now).LastOrDefault(OfSeries);
            if (last is not null)
            {
                return last;
            }
        }

        return null;
    }
}

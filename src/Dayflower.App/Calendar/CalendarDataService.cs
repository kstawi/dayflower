// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Display;
using Dayflower.Core.Model;
using Dayflower.Core.Providers;
using Dayflower.Core.Recurrence;
using Dayflower.Core.Search;
using Dayflower.Services;
using Dayflower.ViewModels;
using Microsoft.Extensions.Logging;

namespace Dayflower.Calendar;

/// <summary>
/// Reads what the calendar views show from the local cache: visible calendars only, repeating events expanded,
/// declined invitations hidden (unless shown on purpose), and copies of one meeting on several accounts merged.
/// Also searches the whole cache.
/// </summary>
/// <param name="showDeclined">Whether declined invitations are shown (struck through) rather than hidden.</param>
public sealed class CalendarDataService(AppServices services, Func<bool>? showDeclined = null)
{
    private readonly ILogger _logger = services.Loggers.CreateLogger<CalendarDataService>();
    private readonly Dictionary<string, ICalendarProvider> _providers = services.Providers.ToDictionary(p => p.Id);
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private EventSearchIndex? _index;

    /// <summary>Items between <paramref name="first"/> and <paramref name="afterLast"/>, in local time. Safe off the UI thread.</summary>
    public async Task<IReadOnlyList<CalendarItem>> LoadAsync(DateOnly first, DateOnly afterLast, CancellationToken cancellationToken = default)
    {
        var zone = TimeZoneInfo.Local;
        var from = LocalDays.StartUtc(first, zone);
        var to = LocalDays.StartUtc(afterLast, zone);

        var context = await LoadContextAsync(cancellationToken);
        var events = Parse(await services.Store.GetEventsAsync(from, to, cancellationToken), context);
        var items = BuildItems(OccurrenceResolver.Resolve(events, from, to), context);
        return items.Where(i => i.Span.Overlaps(first, afterLast)).ToList();
    }

    /// <summary>
    /// Events matching <paramref name="query"/> across all visible calendars, upcoming first, then past ones. The
    /// first search reads the whole cache; later ones reuse it until <see cref="InvalidateSearch"/>.
    /// </summary>
    public async Task<IReadOnlyList<CalendarItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        var context = await LoadContextAsync(cancellationToken);
        EventSearchIndex index;
        await _indexLock.WaitAsync(cancellationToken);
        try
        {
            if (_index is null)
            {
                var all = await services.Store.GetEventsAsync(DateTimeOffset.UnixEpoch, new DateTimeOffset(2200, 1, 1, 0, 0, 0, TimeSpan.Zero), cancellationToken);
                _index = new EventSearchIndex(Parse(all, context));
            }

            index = _index;
        }
        finally
        {
            _indexLock.Release();
        }

        var now = DateTimeOffset.Now;
        var items = BuildItems(index.Find(query, now), context);

        // Merging duplicates loses the order, so it's put back: upcoming soonest first, then past latest first.
        var nowLocal = now.LocalDateTime;
        return items.Where(i => i.Span.End > nowLocal).OrderBy(i => i.Span.Start)
            .Concat(items.Where(i => i.Span.End <= nowLocal).OrderByDescending(i => i.Span.Start))
            .ToList();
    }

    /// <summary>Drops the search index after the cache or the visible calendars change.</summary>
    public void InvalidateSearch() => _index = null;

    private sealed record Context(
        IReadOnlyDictionary<string, int> AccountOrder,
        IReadOnlyDictionary<string, string> Emails,
        IReadOnlyDictionary<(string AccountId, string Id), CalendarRecord> Calendars);

    private async Task<Context> LoadContextAsync(CancellationToken cancellationToken)
    {
        var accounts = await services.Store.GetAccountsAsync(cancellationToken);
        var calendars = (await services.Store.GetCalendarsAsync(null, cancellationToken)).ToDictionary(c => (c.AccountId, c.Id));
        return new Context(
            accounts.Select((a, i) => (a.Id, i)).ToDictionary(x => x.Id, x => x.i),
            accounts.ToDictionary(a => a.Id, a => a.Email),
            calendars);
    }

    /// <summary>The cached events of visible calendars, parsed; unreadable ones are skipped.</summary>
    private List<CalendarEvent> Parse(IEnumerable<Core.Storage.StoredEvent> stored, Context context)
    {
        var events = new List<CalendarEvent>();
        foreach (var e in stored)
        {
            if (!context.Calendars.TryGetValue((e.Key.AccountId, e.Key.CalendarId), out var calendar) || !calendar.IsVisible
                || !_providers.TryGetValue(e.ProviderId, out var provider))
            {
                continue;
            }

            try
            {
                events.Add(provider.ParseEvent(e.Key.AccountId, e.Key.CalendarId, e.Payload));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping unreadable cached event {Event}", e.Key.EventId);
            }
        }

        return events;
    }

    private List<CalendarItem> BuildItems(IEnumerable<EventOccurrence> occurrences, Context context)
    {
        bool declined = showDeclined?.Invoke() == true;
        var zone = TimeZoneInfo.Local;
        var shown = occurrences.Where(o => declined || o.Event.Self?.Response != AttendeeResponse.Declined);
        var merged = DuplicateMerger.Merge(shown, id => context.AccountOrder.GetValueOrDefault(id, int.MaxValue));

        var result = new List<CalendarItem>(merged.Count);
        foreach (var m in merged)
        {
            var span = LocalSpan.Of(m.Primary.Start, m.Primary.End, zone);
            var copies = m.Copies.Select(c =>
            {
                var calendar = context.Calendars[(c.Event.Key.AccountId, c.Event.Key.CalendarId)];
                return new CalendarCopy(calendar.Name, context.Emails.GetValueOrDefault(c.Event.Key.AccountId, string.Empty),
                                        ColorOf(c.Event, calendar), c.Event.Self?.Response, calendar.Access, calendar.IsPrimary);
            }).ToList();
            result.Add(new CalendarItem(m, span, copies[0].Color, copies));
        }

        return result;
    }

    /// <summary>The event's own colour if it has one, otherwise its calendar's.</summary>
    private Windows.UI.Color ColorOf(CalendarEvent e, CalendarRecord calendar)
    {
        string? hex = e.ColorId is { } id && _providers.TryGetValue(calendar.AccountId.Split(':')[0], out var provider)
            ? provider.EventColor(id)
            : null;
        return CalendarItemViewModel.ParseColor(hex ?? calendar.BackgroundColor);
    }
}

// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Display;
using Dayflower.Core.Model;
using Dayflower.Core.Providers;
using Dayflower.Core.Recurrence;
using Dayflower.Services;
using Dayflower.ViewModels;
using Microsoft.Extensions.Logging;

namespace Dayflower.Calendar;

/// <summary>
/// Reads what the calendar views show for a range of days from the local cache: visible calendars only, repeating
/// events expanded, declined invitations hidden, and copies of one meeting on several accounts merged.
/// </summary>
public sealed class CalendarDataService(AppServices services)
{
    private readonly ILogger _logger = services.Loggers.CreateLogger<CalendarDataService>();
    private readonly Dictionary<string, ICalendarProvider> _providers = services.Providers.ToDictionary(p => p.Id);

    /// <summary>Items between <paramref name="first"/> and <paramref name="afterLast"/>, in local time. Safe off the UI thread.</summary>
    public async Task<IReadOnlyList<CalendarItem>> LoadAsync(DateOnly first, DateOnly afterLast, CancellationToken cancellationToken = default)
    {
        var zone = TimeZoneInfo.Local;
        var from = LocalDays.StartUtc(first, zone);
        var to = LocalDays.StartUtc(afterLast, zone);

        var accounts = await services.Store.GetAccountsAsync(cancellationToken);
        var order = accounts.Select((a, i) => (a.Id, i)).ToDictionary(x => x.Id, x => x.i);
        var emails = accounts.ToDictionary(a => a.Id, a => a.Email);
        var calendars = (await services.Store.GetCalendarsAsync(null, cancellationToken))
            .ToDictionary(c => (c.AccountId, c.Id));

        var events = new List<CalendarEvent>();
        foreach (var stored in await services.Store.GetEventsAsync(from, to, cancellationToken))
        {
            if (!calendars.TryGetValue((stored.Key.AccountId, stored.Key.CalendarId), out var calendar) || !calendar.IsVisible
                || !_providers.TryGetValue(stored.ProviderId, out var provider))
            {
                continue;
            }

            try
            {
                events.Add(provider.ParseEvent(stored.Key.AccountId, stored.Key.CalendarId, stored.Payload));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping unreadable cached event {Event}", stored.Key.EventId);
            }
        }

        var occurrences = OccurrenceResolver.Resolve(events, from, to)
            .Where(o => o.Event.Self?.Response != AttendeeResponse.Declined);
        var merged = DuplicateMerger.Merge(occurrences, id => order.GetValueOrDefault(id, int.MaxValue));

        var result = new List<CalendarItem>(merged.Count);
        foreach (var m in merged)
        {
            var span = LocalSpan.Of(m.Primary.Start, m.Primary.End, zone);
            if (!span.Overlaps(first, afterLast))
            {
                continue;
            }

            var copies = m.Copies.Select(c =>
            {
                var calendar = calendars[(c.Event.Key.AccountId, c.Event.Key.CalendarId)];
                return new CalendarCopy(calendar.Name, emails.GetValueOrDefault(c.Event.Key.AccountId, string.Empty),
                                        ColorOf(c.Event, calendar), c.Event.Self?.Response);
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

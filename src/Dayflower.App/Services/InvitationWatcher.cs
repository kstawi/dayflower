// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Text.Json;
using Dayflower.Calendar;
using Dayflower.Core.Editing;
using Dayflower.Core.Model;
using Dayflower.Core.Providers;
using Dayflower.Core.Recurrence;
using Dayflower.Core.Reminders;
using Dayflower.Views;
using Microsoft.Extensions.Logging;

namespace Dayflower.Services;

/// <summary>
/// After each sync, announces invitations that arrived since the last one, with Accept / Maybe / Decline. What's
/// been seen is kept in invitations.json; invitations already there when an account is added aren't announced.
/// </summary>
public sealed class InvitationWatcher(
    AppServices services, AppSettings settings, NotificationService notifications, EventEditService edits, ILogger<InvitationWatcher> logger)
{
    private const int MaxNotifications = 5;

    private readonly Dictionary<string, ICalendarProvider> _providers = services.Providers.ToDictionary(p => p.Id);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task CheckAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var now = DateTimeOffset.Now;
            var (events, synced) = await Task.Run(() => LoadAsync(now));
            var state = State.Load();
            var announce = InvitationWatch.FindNew(events, now, state.Known, state.Seeded, synced);
            state.Save();
            if (!settings.InvitationNotifications || announce.Count == 0)
            {
                return;
            }

            foreach (var e in announce.Take(MaxNotifications))
            {
                notifications.ShowInvitation(new InvitationToast(
                    InvitationWatch.Key(e), string.IsNullOrWhiteSpace(e.Summary) ? "(No title)" : e.Summary.Trim(), When(e, now),
                    e.Organizer?.DisplayName ?? e.Organizer?.Email));
            }

            if (announce.Count > MaxNotifications)
            {
                notifications.ShowNotice("More invitations", $"{announce.Count - MaxNotifications} more invitations are waiting in Dayflower.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Checking for new invitations failed");
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Answers an invitation from its notification; a series is answered for all its occurrences.</summary>
    public async Task RespondAsync(string? key, string? response)
    {
        var answer = response switch
        {
            "accepted" => AttendeeResponse.Accepted,
            "tentative" => AttendeeResponse.Tentative,
            "declined" => AttendeeResponse.Declined,
            _ => (AttendeeResponse?)null,
        };
        if (key?.Split('|', 3) is not [var account, var calendar, var id] || answer is null)
        {
            return;
        }

        var e = await LoadEventAsync(new EventKey(account, calendar, id));
        if (e is null)
        {
            notifications.ShowNotice("Invitation not found", "It may have been cancelled or moved. Open Dayflower to see your calendar.");
            return;
        }

        try
        {
            await edits.RespondAsync(new EventTarget(e, null, e.Start, e.End), new Rsvp(answer.Value, e.Self?.Comment),
                                     e.IsSeries ? EditScope.AllEvents : EditScope.ThisEvent);
            logger.LogInformation("Answered {Key} with {Response} from a notification", key, response);
        }
        catch (EditFailedException ex)
        {
            notifications.ShowNotice($"Couldn't answer “{e.Summary}”", $"{ex.Title}. {ex.Message}");
        }
    }

    /// <summary>The day an invitation is on, to open the calendar there.</summary>
    public async Task<DateOnly?> FindDayAsync(string? key)
    {
        if (key?.Split('|', 3) is not [var account, var calendar, var id] || await LoadEventAsync(new EventKey(account, calendar, id)) is not { } e)
        {
            return null;
        }

        var now = DateTimeOffset.Now;
        var upcoming = e.IsSeries ? RecurrenceExpander.Expand(e, now, now.AddYears(1)) : [];
        var start = upcoming.Count > 0 ? upcoming[0].Start : e.Start;
        return start.Date ?? DateOnly.FromDateTime(start.DateTime!.Value.LocalDateTime);
    }

    /// <summary>
    /// Events of the user's own visible main calendars that may still happen, and the accounts synced at least once.
    /// </summary>
    private async Task<(List<CalendarEvent> Events, List<string> Synced)> LoadAsync(DateTimeOffset now)
    {
        var accounts = await services.Store.GetAccountsAsync();
        var synced = accounts.Where(a => a.LastSyncedUtc is not null && a.State == AccountState.Ok).Select(a => a.Id).ToList();
        var own = (await services.Store.GetCalendarsAsync())
            .Where(c => c.IsPrimary && c.IsVisible)
            .Select(c => (c.AccountId, c.Id))
            .ToHashSet();

        var events = new List<CalendarEvent>();
        foreach (var stored in await services.Store.GetEventsAsync(now, now.AddYears(1)))
        {
            if (own.Contains((stored.Key.AccountId, stored.Key.CalendarId)) && _providers.TryGetValue(stored.ProviderId, out var provider))
            {
                try
                {
                    events.Add(provider.ParseEvent(stored.Key.AccountId, stored.Key.CalendarId, stored.Payload));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Skipping unreadable cached event {Event}", stored.Key.EventId);
                }
            }
        }

        return (events, synced);
    }

    private async Task<CalendarEvent?> LoadEventAsync(EventKey key)
    {
        var stored = await services.Store.GetEventAsync(key);
        return stored is not null && _providers.TryGetValue(stored.ProviderId, out var provider)
            ? provider.ParseEvent(key.AccountId, key.CalendarId, stored.Payload)
            : null;
    }

    /// <summary>"Friday, 13 November · 10:00", with "(repeats)" for a series at its next occurrence.</summary>
    private static string When(CalendarEvent e, DateTimeOffset now)
    {
        var upcoming = e.IsSeries ? RecurrenceExpander.Expand(e, now, now.AddYears(1)) : [];
        var (start, end) = upcoming.Count > 0 ? (upcoming[0].Start, upcoming[0].End) : (e.Start, e.End);
        string text;
        if (start.IsAllDay)
        {
            text = $"{TimeText.LongDate(start.Date!.Value)} · All day";
        }
        else
        {
            var local = start.DateTime!.Value.LocalDateTime;
            text = $"{TimeText.LongDate(DateOnly.FromDateTime(local))} · {TimeText.TimeRange(local, end.DateTime!.Value.LocalDateTime)}";
        }

        return e.IsSeries ? $"{text} (repeats)" : text;
    }

    /// <summary>Invitations seen and accounts looked at, kept between runs.</summary>
    private sealed class State
    {
        public HashSet<string> Known { get; set; } = [];

        public HashSet<string> Seeded { get; set; } = [];

        private static string FilePath => Path.Combine(AppPaths.DataFolder, "invitations.json");

        public static State Load()
        {
            try
            {
                return File.Exists(FilePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) ?? new State() : new State();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return new State();
            }
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
            }
            catch (IOException)
            {
                // Not fatal: at worst an invitation is announced again.
            }
        }
    }
}

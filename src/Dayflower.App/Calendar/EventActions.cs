// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Editing;
using Dayflower.Core.Model;
using Dayflower.Core.Recurrence;
using Dayflower.Services;
using Dayflower.ViewModels;
using Dayflower.Views;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Dayflower.Calendar;

/// <summary>
/// What the user can do to events, from start to finish: the questions to ask (which occurrences, whether to email
/// guests, a confirmation), the editor, the change itself, and telling the user if it didn't work.
/// </summary>
public sealed class EventActions(AppServices services, EventEditService edits, AppSettings settings, Func<FrameworkElement> host)
{
    private readonly ILogger _logger = services.Loggers.CreateLogger<EventActions>();
    private int _working;

    /// <summary>Raised on the UI thread when a change starts or finishes going to the provider.</summary>
    public event EventHandler<bool>? WorkingChanged;

    public static bool IsOnline => EventEditService.IsOnline;

    private XamlRoot Root => host().XamlRoot;

    private ElementTheme Theme => host().ActualTheme;

    /// <summary>Calendars new events can go to: visible ones the user can write to, in side-pane order.</summary>
    public async Task<IReadOnlyList<EditorCalendar>> GetWritableCalendarsAsync()
    {
        var all = await GetCalendarsAsync();
        return all.Where(c => c.Calendar.Access >= CalendarAccess.Writer && c.Calendar.IsVisible).ToList();
    }

    /// <summary>
    /// The calendar to suggest for a new event: the one chosen in Settings, else the last one used, else the first
    /// account's main calendar.
    /// </summary>
    public EditorCalendar? DefaultCalendar(IReadOnlyList<EditorCalendar> calendars) =>
        calendars.FirstOrDefault(c => $"{c.AccountId}|{c.Id}" == settings.DefaultCalendar)
        ?? calendars.FirstOrDefault(c => $"{c.AccountId}|{c.Id}" == settings.LastCalendar)
        ?? calendars.FirstOrDefault(c => c.Calendar.IsPrimary)
        ?? calendars.FirstOrDefault();

    /// <summary>Opens the editor for a new event; saves it if the user does.</summary>
    public async Task NewEventAsync(NewEventDraft? draft)
    {
        if (!await EnsureOnlineAsync())
        {
            return;
        }

        var calendars = await GetWritableCalendarsAsync();
        if (calendars.Count == 0)
        {
            await EditPrompts.ShowErrorAsync(Root, Theme, "No calendar to add to", "None of your visible calendars lets you add events.");
            return;
        }

        draft ??= new NewEventDraft(DateTime.Now.Date.AddHours(DateTime.Now.Hour + 1), DateTime.Now.Date.AddHours(DateTime.Now.Hour + 2), false);
        draft = draft with { Calendar = draft.Calendar ?? DefaultCalendar(calendars) };
        var editor = new EventEditor(Root, Theme, edits, services.Google.EventColor, calendars, null, null, draft);
        while (await editor.ShowAsync() is { } result)
        {
            if (await CreateAsync(result.Calendar, result.Edit))
            {
                return;
            }
        }
    }

    /// <summary>Saves a new event as it is (from the quick-create flyout). Returns whether it was saved.</summary>
    public async Task<bool> CreateAsync(EditorCalendar calendar, EventEdit edit)
    {
        bool notify = false;
        if (edit.Guests is { Count: > 0 })
        {
            if (await EditPrompts.AskNotifyAsync(Root, Theme, GuestNotice.Invitation) is not { } send)
            {
                return false;
            }

            notify = send;
        }

        settings.LastCalendar = $"{calendar.AccountId}|{calendar.Id}";
        settings.Save();
        return await RunAsync(() => edits.CreateAsync(calendar.AccountId, calendar.Id, edit, notify));
    }

    /// <summary>Opens the editor for an event; saves what changed.</summary>
    public async Task EditAsync(CalendarItem item)
    {
        if (item.EditableCopy is not { } copy || !await EnsureOnlineAsync())
        {
            return;
        }

        var calendar = (await GetCalendarsAsync()).FirstOrDefault(c => c.AccountId == copy.Event.Key.AccountId && c.Id == copy.Event.Key.CalendarId);
        if (calendar is null)
        {
            return;
        }

        IReadOnlyList<string>? seriesRecurrence = null;
        if (copy.Event.IsOccurrenceOverride && copy.Event.RecurringEventId is { } seriesId)
        {
            seriesRecurrence = (await LoadEventAsync(copy.Event.Key with { EventId = seriesId }))?.Recurrence;
        }

        var editor = new EventEditor(Root, Theme, edits, services.Google.EventColor, [calendar], copy, calendar, null, seriesRecurrence);
        while (await editor.ShowAsync() is { } result)
        {
            if (result.Edit.IsEmpty || await UpdateAsync(copy, result.Edit))
            {
                return;
            }
        }
    }

    /// <summary>Moves or resizes an event to new wall-clock times in Windows' zone (from dragging).</summary>
    public async Task<bool> MoveAsync(CalendarItem item, DateTime start, DateTime end)
    {
        if (item.EditableCopy is not { } copy || !await EnsureOnlineAsync())
        {
            return false;
        }

        EventEdit edit;
        if (copy.Start.IsAllDay)
        {
            edit = new EventEdit
            {
                Start = EventTime.AllDay(DateOnly.FromDateTime(start)),
                End = EventTime.AllDay(DateOnly.FromDateTime(end)),
            };
        }
        else
        {
            string? zone = copy.Event.Start.TimeZone;
            var startTime = TimeZones.At(start, null);
            var endTime = TimeZones.At(end, null);
            edit = new EventEdit
            {
                Start = EventTime.At(startTime.DateTime!.Value, zone ?? TimeZones.LocalId),
                End = EventTime.At(endTime.DateTime!.Value, zone ?? TimeZones.LocalId),
            };
        }

        return await UpdateAsync(copy, edit);
    }

    /// <summary>Deletes the event, or for someone else's invitation declines it (which hides it).</summary>
    public async Task DeleteAsync(CalendarItem item)
    {
        if (!await EnsureOnlineAsync())
        {
            return;
        }

        if (item.DeletableCopy is { } copy)
        {
            var target = Target(copy);
            var scope = EditScope.ThisEvent;
            bool asked = false;
            if (target.IsRepeating)
            {
                if (await EditPrompts.AskScopeAsync(Root, Theme, "Delete repeating event") is not { } chosen)
                {
                    return;
                }

                scope = chosen;
                asked = true;
            }

            bool notify = false;
            if (copy.Event.OtherGuests.Any())
            {
                if (await EditPrompts.AskNotifyAsync(Root, Theme, GuestNotice.Cancellation) is not { } send)
                {
                    return;
                }

                notify = send;
                asked = true;
            }

            if (!asked && !await EditPrompts.ConfirmAsync(Root, Theme, "Delete event?", $"“{item.Title}” will be deleted from {item.Copies[0].CalendarName}.", "Delete"))
            {
                return;
            }

            await RunAsync(() => edits.DeleteAsync(target, scope, notify));
        }
        else if (item.RespondableCopy is not null)
        {
            await RespondAsync(item, AttendeeResponse.Declined);
        }
    }

    /// <summary>Answers an invitation, asking first which occurrences for a repeating one.</summary>
    public async Task RespondAsync(CalendarItem item, AttendeeResponse response, string? comment = null)
    {
        if (item.RespondableCopy is not { } copy || !await EnsureOnlineAsync())
        {
            return;
        }

        var target = Target(copy);
        var scope = EditScope.ThisEvent;
        if (target.IsRepeating)
        {
            if (await EditPrompts.AskScopeAsync(Root, Theme, "Reply to repeating event", allowFollowing: false) is not { } chosen)
            {
                return;
            }

            scope = chosen;
        }

        // Keep an existing note unless a new one is given.
        await RunAsync(() => edits.RespondAsync(target, new Rsvp(response, comment ?? copy.Event.Self?.Comment), scope));
    }

    /// <summary>Answers an invitation with a note for the organiser.</summary>
    public async Task RespondWithNoteAsync(CalendarItem item)
    {
        if (item.RespondableCopy is not { } copy || !await EnsureOnlineAsync())
        {
            return;
        }

        if (await EditPrompts.AskRsvpWithNoteAsync(Root, Theme, copy.Event.Self?.Response, copy.Event.Self?.Comment) is { } rsvp)
        {
            await RespondAsync(item, rsvp.Response, rsvp.Comment ?? string.Empty);
        }
    }

    private async Task<bool> UpdateAsync(EventOccurrence copy, EventEdit edit)
    {
        var target = Target(copy);
        var scope = EditScope.ThisEvent;
        if (target.IsRepeating)
        {
            // A new repeat rule can't apply to one occurrence alone.
            if (await EditPrompts.AskScopeAsync(Root, Theme, "Edit repeating event", allowThis: edit.Recurrence is null) is not { } chosen)
            {
                return false;
            }

            scope = chosen;
        }

        bool notify = false;
        bool hadGuests = copy.Event.OtherGuests.Any();
        bool hasGuests = edit.Guests is { } guests ? guests.Count > 0 : hadGuests;
        if ((hadGuests || hasGuests) && edit.AffectsGuests)
        {
            if (await EditPrompts.AskNotifyAsync(Root, Theme, hadGuests ? GuestNotice.Update : GuestNotice.Invitation) is not { } send)
            {
                return false;
            }

            notify = send;
        }

        return await RunAsync(() => edits.UpdateAsync(target, edit, scope, notify));
    }

    private static EventTarget Target(EventOccurrence copy) => new(copy.Event, copy.OriginalStart, copy.Start, copy.End);

    private async Task<bool> RunAsync(Func<Task> change)
    {
        if (++_working == 1)
        {
            WorkingChanged?.Invoke(this, true);
        }

        try
        {
            await change();
            return true;
        }
        catch (EditFailedException ex)
        {
            await EditPrompts.ShowErrorAsync(Root, Theme, ex.Title, ex.Message);
            return false;
        }
        finally
        {
            if (--_working == 0)
            {
                WorkingChanged?.Invoke(this, false);
            }
        }
    }

    private async Task<bool> EnsureOnlineAsync()
    {
        if (IsOnline)
        {
            return true;
        }

        await EditPrompts.ShowErrorAsync(Root, Theme, "You're offline", "Changes need an internet connection. Your calendar is still here to look at.");
        return false;
    }

    private async Task<IReadOnlyList<EditorCalendar>> GetCalendarsAsync()
    {
        var accounts = await services.Store.GetAccountsAsync();
        var calendars = await services.Store.GetCalendarsAsync();
        return accounts
            .Where(a => a.State == AccountState.Ok)
            .SelectMany(a => calendars
                .Where(c => c.AccountId == a.Id)
                .OrderByDescending(c => c.IsPrimary)
                .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(c => new EditorCalendar(c, a.Email, CalendarItemViewModel.ParseColor(c.BackgroundColor))))
            .ToList();
    }

    private async Task<CalendarEvent?> LoadEventAsync(EventKey key)
    {
        try
        {
            var stored = await services.Store.GetEventAsync(key);
            return stored is null ? null : services.Google.ParseEvent(key.AccountId, key.CalendarId, stored.Payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't read the series {Series}", key.EventId);
            return null;
        }
    }
}

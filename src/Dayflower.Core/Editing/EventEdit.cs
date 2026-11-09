// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Model;

namespace Dayflower.Core.Editing;

/// <summary>A guest in an edit: who, and whether they're optional.</summary>
public sealed record GuestEdit(string Email, bool IsOptional = false);

/// <summary>
/// A change to an event, or a new event. Only the properties that are set are sent, so an edit never overwrites
/// what the user didn't touch (such as a description with formatting Dayflower doesn't show).
/// </summary>
public sealed record EventEdit
{
    public string? Summary { get; init; }

    public string? Location { get; init; }

    public string? Description { get; init; }

    /// <summary>Start and end are set together.</summary>
    public EventTime? Start { get; init; }

    public EventTime? End { get; init; }

    /// <summary>iCalendar recurrence lines; an empty list makes the event stop repeating.</summary>
    public IReadOnlyList<string>? Recurrence { get; init; }

    /// <summary>The full guest list (not counting the user, who is kept as they are).</summary>
    public IReadOnlyList<GuestEdit>? Guests { get; init; }

    public bool? GuestsCanModify { get; init; }

    public bool? GuestsCanInviteOthers { get; init; }

    public bool? GuestsCanSeeOtherGuests { get; init; }

    /// <summary>True adds a Google Meet link, false removes it.</summary>
    public bool? Meet { get; init; }

    public ReminderSettings? Reminders { get; init; }

    /// <summary>An event colour ID, or an empty string for the calendar's colour.</summary>
    public string? ColorId { get; init; }

    public bool? IsBusy { get; init; }

    public EventVisibility? Visibility { get; init; }

    /// <summary>For new events: an out of office, focus time or working location event instead of a normal one.</summary>
    public EventKind? Kind { get; init; }

    public WorkspaceDetails? Workspace { get; init; }

    public bool IsEmpty => this == new EventEdit();

    /// <summary>Whether the change is one guests get notified about (time, place, title, guests, recurrence).</summary>
    public bool AffectsGuests => Summary is not null || Location is not null || Description is not null || Start is not null
                                 || End is not null || Recurrence is not null || Guests is not null || Meet is not null;
}

/// <summary>For an occurrence of a repeating event: which occurrences a change applies to.</summary>
public enum EditScope
{
    ThisEvent,
    ThisAndFollowing,
    AllEvents,
}

/// <summary>
/// The event a change is aimed at, as it was shown: a one-off event, an occurrence of a series (the series plus
/// the occurrence's original start), or a changed occurrence.
/// </summary>
public sealed record EventTarget(CalendarEvent Event, EventTime? OriginalStart, EventTime OccurrenceStart, EventTime OccurrenceEnd)
{
    public string CalendarId => Event.Key.CalendarId;

    /// <summary>Whether the target is one occurrence of a repeating event, so a scope has to be chosen.</summary>
    public bool IsRepeating => Event.IsSeries || Event.IsOccurrenceOverride;

    /// <summary>The series' ID for repeating events.</summary>
    public string? SeriesId => Event.IsSeries ? Event.Key.EventId : Event.RecurringEventId;
}

/// <summary>An answer to an invitation.</summary>
public sealed record Rsvp(AttendeeResponse Response, string? Comment = null);

// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

namespace Dayflower.Core.Model;

/// <summary>Identifies an event: which account, which calendar, and the provider's ID for it.</summary>
public readonly record struct EventKey(string AccountId, string CalendarId, string EventId);

public enum EventStatus
{
    Confirmed,
    Tentative,
    Cancelled,
}

public enum EventKind
{
    Default,
    OutOfOffice,
    FocusTime,
    WorkingLocation,
    Birthday,
    FromGmail,
}

public enum EventVisibility
{
    Default,
    Public,
    Private,
    Confidential,
}

public enum AttendeeResponse
{
    NeedsAction,
    Declined,
    Tentative,
    Accepted,
}

public sealed record EventPerson(string? Email, string? DisplayName, bool IsSelf);

public sealed record Attendee(
    string? Email,
    string? DisplayName,
    AttendeeResponse Response,
    bool IsSelf,
    bool IsOrganizer,
    bool IsOptional,
    bool IsResource,
    string? Comment);

public enum ReminderMethod
{
    Popup,
    Email,
}

public sealed record Reminder(ReminderMethod Method, int MinutesBefore);

/// <summary>
/// An event's reminders: either its calendar's default reminders, or its own list.
/// </summary>
public sealed record ReminderSettings(bool UseDefault, IReadOnlyList<Reminder> Overrides)
{
    public static ReminderSettings Default { get; } = new(true, []);
}

/// <summary>One way to join a meeting, e.g. a video link or a dial-in number.</summary>
public sealed record ConferenceEntryPoint(string Type, string Uri, string? Label);

/// <summary>Meeting details a provider attached to the event (Google Meet, or Zoom/Teams through add-ons).</summary>
public sealed record ConferenceInfo(string? SolutionName, IReadOnlyList<ConferenceEntryPoint> EntryPoints);

/// <summary>
/// A calendar event as Dayflower sees it, whatever provider it came from. A repeating event appears once, as its
/// series (<see cref="Recurrence"/> set); changed or cancelled occurrences are separate events that point back to
/// it with <see cref="RecurringEventId"/> and <see cref="OriginalStart"/>.
/// </summary>
public sealed record CalendarEvent
{
    public required EventKey Key { get; init; }

    /// <summary>iCalendar UID. Copies of one meeting on different accounts share it.</summary>
    public string? ICalUid { get; init; }

    public EventStatus Status { get; init; } = EventStatus.Confirmed;

    public string? Summary { get; init; }

    public string? Location { get; init; }

    /// <summary>Description as the provider stores it; Google uses limited HTML.</summary>
    public string? Description { get; init; }

    public required EventTime Start { get; init; }

    public required EventTime End { get; init; }

    /// <summary>iCalendar recurrence lines (RRULE, EXDATE, RDATE) for a series; empty otherwise.</summary>
    public IReadOnlyList<string> Recurrence { get; init; } = [];

    /// <summary>For a changed or cancelled occurrence: the ID of its series.</summary>
    public string? RecurringEventId { get; init; }

    /// <summary>For a changed or cancelled occurrence: when it would have started without the change.</summary>
    public EventTime? OriginalStart { get; init; }

    public EventPerson? Organizer { get; init; }

    public IReadOnlyList<Attendee> Attendees { get; init; } = [];

    public ReminderSettings Reminders { get; init; } = ReminderSettings.Default;

    public ConferenceInfo? Conference { get; init; }

    /// <summary>Google Meet link Google adds to events with Meet conferencing.</summary>
    public string? HangoutLink { get; init; }

    /// <summary>The event's own colour ID, if it overrides its calendar's colour.</summary>
    public string? ColorId { get; init; }

    /// <summary>Whether the event blocks time (shows as busy).</summary>
    public bool IsBusy { get; init; } = true;

    public EventVisibility Visibility { get; init; } = EventVisibility.Default;

    public EventKind Kind { get; init; } = EventKind.Default;

    /// <summary>Link to the event in the provider's web app.</summary>
    public string? WebLink { get; init; }

    public DateTimeOffset? Updated { get; init; }

    public bool IsSeries => Recurrence.Count > 0;

    public bool IsOccurrenceOverride => RecurringEventId is not null;

    /// <summary>The signed-in user's own attendee entry, if they're invited.</summary>
    public Attendee? Self => Attendees.FirstOrDefault(a => a.IsSelf);
}

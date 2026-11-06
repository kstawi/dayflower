// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Display;
using Dayflower.Core.Meetings;
using Dayflower.Core.Model;
using Dayflower.Core.Recurrence;
using Windows.UI;

namespace Dayflower.Calendar;

/// <summary>Where one copy of a meeting lives: which calendar of which account, and how the user answered there.</summary>
public sealed record CalendarCopy(string CalendarName, string AccountEmail, Color Color, AttendeeResponse? Response, CalendarAccess Access);

/// <summary>Something to draw in a calendar view: one (possibly merged) occurrence, in the viewer's local time.</summary>
public sealed class CalendarItem(MergedOccurrence merged, LocalSpan span, Color color, IReadOnlyList<CalendarCopy> copies)
{
    public MergedOccurrence Merged { get; } = merged;

    /// <summary>The event the details come from.</summary>
    public CalendarEvent Event => Merged.Primary.Event;

    public LocalSpan Span { get; } = span;

    public Color Color { get; } = color;

    /// <summary>Every copy, the primary one first.</summary>
    public IReadOnlyList<CalendarCopy> Copies { get; } = copies;

    public string Title => string.IsNullOrWhiteSpace(Event.Summary) ? "(No title)" : Event.Summary.Trim();

    public AttendeeResponse? MyResponse => Event.Self?.Response;

    /// <summary>An occurrence of a repeating event (unchanged or changed).</summary>
    public bool IsRepeating => Event.IsSeries || Event.IsOccurrenceOverride;

    /// <summary>The video meeting to join, from whichever copy has one (normally all of them).</summary>
    public MeetingLink? Meeting { get; } = merged.Copies.Select(c => MeetingLinkDetector.Find(c.Event)).FirstOrDefault(l => l is not null);

    /// <summary>The user's accounts this meeting is on, the primary copy's first; each can join as itself.</summary>
    public IReadOnlyList<string> Accounts => Copies.Select(c => c.AccountEmail).Where(a => a.Length > 0).Distinct().ToList();

    /// <summary>The copy changes go to: the first the user may edit (normally the organiser's), or null if none.</summary>
    public EventOccurrence? EditableCopy => Merged.Copies.Where((c, i) => c.Event.CanEdit(Copies[i].Access)).FirstOrDefault();

    /// <summary>The copy the user may delete outright, or null (an invitation is declined instead).</summary>
    public EventOccurrence? DeletableCopy => Merged.Copies.Where((c, i) => c.Event.CanDelete(Copies[i].Access)).FirstOrDefault();

    /// <summary>The copy whose invitation the user can answer, or null.</summary>
    public EventOccurrence? RespondableCopy => Merged.Copies.FirstOrDefault(c => c.Event.CanRespond);

    /// <summary>Whether Join should show on the event in the time grid: from <paramref name="lead"/> before the start until the end.</summary>
    public bool IsJoinable(DateTime now, TimeSpan lead) => Meeting is not null && now >= Span.Start - lead && now < Span.End;
}

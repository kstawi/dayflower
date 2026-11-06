// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Display;
using Dayflower.Core.Model;
using Windows.UI;

namespace Dayflower.Calendar;

/// <summary>Where one copy of a meeting lives: which calendar of which account, and how the user answered there.</summary>
public sealed record CalendarCopy(string CalendarName, string AccountEmail, Color Color, AttendeeResponse? Response);

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
}

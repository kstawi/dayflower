// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using Dayflower.Core.Model;

namespace Dayflower.Core.Tests;

/// <summary>Builders for test events.</summary>
internal static class TestEvents
{
    public const string Warsaw = "Europe/Warsaw";

    public static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);

    public static DateTimeOffset Utc(int year, int month, int day, int hour = 0, int minute = 0) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    public static CalendarEvent Timed(string id, string start, string end, string? timeZone = Warsaw, params string[] recurrence) => new()
    {
        Key = new EventKey("google:1", "primary", id),
        Summary = id,
        Start = EventTime.At(At(start), timeZone),
        End = EventTime.At(At(end), timeZone),
        Recurrence = recurrence,
    };

    public static CalendarEvent AllDay(string id, DateOnly start, DateOnly end, params string[] recurrence) => new()
    {
        Key = new EventKey("google:1", "primary", id),
        Summary = id,
        Start = EventTime.AllDay(start),
        End = EventTime.AllDay(end),
        Recurrence = recurrence,
    };

    public static CalendarEvent Override(CalendarEvent series, string id, string originalStart, string start, string end,
                                         EventStatus status = EventStatus.Confirmed) => new()
    {
        Key = series.Key with { EventId = id },
        Summary = series.Summary + " (changed)",
        Status = status,
        RecurringEventId = series.Key.EventId,
        OriginalStart = EventTime.At(At(originalStart), series.Start.TimeZone),
        Start = EventTime.At(At(start), series.Start.TimeZone),
        End = EventTime.At(At(end), series.Start.TimeZone),
    };
}

// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Model;

namespace Dayflower.Core.Editing;

/// <summary>
/// Changing the time of one occurrence with "All events" moves the whole series by the same wall-clock difference,
/// in the event's own time zone, so a series that started before a clock change keeps its local time.
/// </summary>
public static class SeriesShift
{
    /// <param name="occurrenceStart">When the edited occurrence was scheduled.</param>
    /// <param name="newStart">Where the user moved it.</param>
    /// <param name="newEnd">Its new end.</param>
    public static (EventTime Start, EventTime End) Move(CalendarEvent series, EventTime occurrenceStart, EventTime newStart, EventTime newEnd)
    {
        string? zoneId = newStart.TimeZone ?? series.Start.TimeZone;
        var zone = Find(zoneId);

        var seriesDay = LocalDate(series.Start, zone);
        var dayShift = LocalDate(newStart, zone).DayNumber - LocalDate(occurrenceStart, zone).DayNumber;
        var firstDay = seriesDay.AddDays(dayShift);

        if (newStart.IsAllDay)
        {
            int days = Math.Max(1, newEnd.Date!.Value.DayNumber - newStart.Date!.Value.DayNumber);
            return (EventTime.AllDay(firstDay), EventTime.AllDay(firstDay.AddDays(days)));
        }

        // Keep the new time of day on the series' (shifted) first day, then the new length.
        var localStart = firstDay.ToDateTime(TimeOnly.FromDateTime(Local(newStart.DateTime!.Value, zone)));
        var start = FromLocal(localStart, zone, newStart.DateTime!.Value.Offset);
        var length = newEnd.DateTime!.Value - newStart.DateTime.Value;
        return (EventTime.At(start, zoneId), EventTime.At(start + length, zoneId));
    }

    private static DateOnly LocalDate(EventTime time, TimeZoneInfo? zone) =>
        time.Date ?? DateOnly.FromDateTime(Local(time.DateTime!.Value, zone));

    private static DateTime Local(DateTimeOffset value, TimeZoneInfo? zone) =>
        zone is null ? value.DateTime : TimeZoneInfo.ConvertTime(value, zone).DateTime;

    private static DateTimeOffset FromLocal(DateTime local, TimeZoneInfo? zone, TimeSpan fallbackOffset)
    {
        if (zone is null)
        {
            return new DateTimeOffset(local, fallbackOffset);
        }

        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static TimeZoneInfo? Find(string? id)
    {
        if (id is null)
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }
}

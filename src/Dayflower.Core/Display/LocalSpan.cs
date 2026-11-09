// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Model;

namespace Dayflower.Core.Display;

/// <summary>
/// When an event happens in the viewer's time zone, as wall-clock times. All-day events run from midnight of their
/// first day to midnight after their last day.
/// </summary>
public readonly record struct LocalSpan(DateTime Start, DateTime End, bool IsAllDay)
{
    public static LocalSpan Of(EventTime start, EventTime end, TimeZoneInfo zone)
    {
        if (start.IsAllDay)
        {
            var first = start.Date!.Value.ToDateTime(TimeOnly.MinValue);
            var after = (end.Date ?? start.Date.Value.AddDays(1)).ToDateTime(TimeOnly.MinValue);
            return new LocalSpan(first, after > first ? after : first.AddDays(1), true);
        }

        var s = TimeZoneInfo.ConvertTime(start.DateTime!.Value, zone).DateTime;
        var e = end.DateTime is { } endTime ? TimeZoneInfo.ConvertTime(endTime, zone).DateTime : s;
        return new LocalSpan(s, e < s ? s : e, false);
    }

    /// <summary>Shown in the all-day area rather than the hour grid: all-day events and anything a day or longer.</summary>
    public bool IsAllDayLike => IsAllDay || End - Start >= TimeSpan.FromDays(1);

    public DateOnly FirstDay => DateOnly.FromDateTime(Start);

    /// <summary>The last day the event touches.</summary>
    public DateOnly LastDay => End > Start ? DateOnly.FromDateTime(End.AddTicks(-1)) : FirstDay;

    public bool Overlaps(DateTime from, DateTime to) => Start < to && (End > from || (Start == End && Start >= from));

    public bool Overlaps(DateOnly first, DateOnly afterLast) =>
        Overlaps(first.ToDateTime(TimeOnly.MinValue), afterLast.ToDateTime(TimeOnly.MinValue));
}

/// <summary>Converts the viewer's local days to UTC moments.</summary>
public static class LocalDays
{
    /// <summary>Midnight at the start of <paramref name="day"/> in <paramref name="zone"/>, as a UTC moment.</summary>
    public static DateTimeOffset StartUtc(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        // Where clocks jump forward at midnight, midnight doesn't exist; the day starts an hour later.
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}

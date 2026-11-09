// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Model;

namespace Dayflower.Calendar;

/// <summary>Time zones as Google names them (IANA, e.g. "Europe/Warsaw"), and wall-clock conversions.</summary>
public static class TimeZones
{
    /// <summary>The IANA name of Windows' own time zone.</summary>
    public static string LocalId { get; } = IanaId(TimeZoneInfo.Local) ?? "UTC";

    public static string? IanaId(TimeZoneInfo zone) =>
        zone.HasIanaId ? zone.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : null;

    public static TimeZoneInfo? Find(string? id)
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

    /// <summary>
    /// The time zones to choose from, as (IANA name, Windows display name), in Windows' order (by UTC offset).
    /// </summary>
    public static IReadOnlyList<(string Id, string Name)> All { get; } = TimeZoneInfo.GetSystemTimeZones()
        .Select(z => (Id: IanaId(z), z.DisplayName))
        .Where(z => z.Id is not null)
        .DistinctBy(z => z.Id)
        .Select(z => (z.Id!, z.DisplayName))
        .ToList();

    /// <summary>A wall-clock time in <paramref name="zoneId"/> (or Windows' zone) as an event time in that zone.</summary>
    public static EventTime At(DateTime wall, string? zoneId)
    {
        var zone = Find(zoneId) ?? TimeZoneInfo.Local;
        var unspecified = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            // A time skipped by a clock change: use the moment just after it.
            unspecified = unspecified.AddHours(1);
        }

        return EventTime.At(new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified)), zoneId ?? LocalId);
    }

    /// <summary>The wall-clock time of <paramref name="value"/> in <paramref name="zoneId"/> (or Windows' zone).</summary>
    public static DateTime WallTime(DateTimeOffset value, string? zoneId) =>
        TimeZoneInfo.ConvertTime(value, Find(zoneId) ?? TimeZoneInfo.Local).DateTime;
}

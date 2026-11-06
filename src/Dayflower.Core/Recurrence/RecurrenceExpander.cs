// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using System.Text;
using Dayflower.Core.Model;
using Ical.Net;
using IcsCalendar = Ical.Net.Calendar;
using Ical.Net.DataTypes;

namespace Dayflower.Core.Recurrence;

/// <summary>One occurrence of a repeating event, before any per-occurrence changes are applied.</summary>
public readonly record struct SeriesOccurrence(EventTime Start, EventTime End);

/// <summary>
/// Expands a repeating event (its iCalendar RRULE, EXDATE and RDATE lines) into occurrences, in the event's own
/// time zone so occurrences keep their wall-clock time across daylight saving changes.
/// </summary>
public static class RecurrenceExpander
{
    /// <summary>Stops runaway rules; no view needs more occurrences of one event than this.</summary>
    private const int MaxOccurrences = 5000;

    /// <summary>Occurrences of <paramref name="series"/> that overlap [<paramref name="from"/>, <paramref name="to"/>).</summary>
    public static IReadOnlyList<SeriesOccurrence> Expand(CalendarEvent series, DateTimeOffset from, DateTimeOffset to)
    {
        if (!series.IsSeries || to <= from)
        {
            return [];
        }

        var calendar = IcsCalendar.Load(ToICalendar(series))
            ?? throw new FormatException($"Couldn't read the recurrence of event {series.Key.EventId}.");
        var vevent = calendar.Events.Single();

        bool allDay = series.Start.IsAllDay;
        TimeZoneInfo? zone = allDay ? null : FindZone(series.Start.TimeZone);

        // Start looking one event-length (plus a day for all-day events) before the range, so occurrences that
        // began earlier but are still going are included.
        CalDateTime searchFrom, searchTo;
        if (allDay)
        {
            int lengthDays = series.End.Date!.Value.DayNumber - series.Start.Date!.Value.DayNumber;
            searchFrom = new CalDateTime(DateOnly.FromDateTime(from.UtcDateTime).AddDays(-Math.Max(lengthDays, 1) - 1));
            searchTo = new CalDateTime(DateOnly.FromDateTime(to.UtcDateTime).AddDays(2));
        }
        else
        {
            var length = series.End.DateTime!.Value - series.Start.DateTime!.Value;
            searchFrom = new CalDateTime((from - length).UtcDateTime, "UTC", true);
            searchTo = new CalDateTime(to.UtcDateTime, "UTC", true);
        }

        var result = new List<SeriesOccurrence>();
        int seen = 0;
        try
        {
            foreach (var occurrence in vevent.GetOccurrences(searchFrom).TakeWhileBefore(searchTo))
            {
                if (++seen > MaxOccurrences)
                {
                    break;
                }

                var period = occurrence.Period;
                SeriesOccurrence item = allDay
                    ? new(EventTime.AllDay(period.StartTime.Date), EventTime.AllDay(period.EffectiveEndTime!.Date))
                    : new(ToEventTime(period.StartTime, zone, series.Start.TimeZone),
                          ToEventTime(period.EffectiveEndTime!, zone, series.Start.TimeZone));

                if (Overlaps(item.Start, item.End, from, to))
                {
                    result.Add(item);
                }
            }
        }
        catch (Ical.Net.Evaluation.EvaluationOutOfRangeException)
        {
            // Rules that run past the dates Ical.Net can represent: keep what was found so far.
        }

        return result;
    }

    /// <summary>Whether an event from <paramref name="start"/> to <paramref name="end"/> shows in the range.</summary>
    public static bool Overlaps(EventTime start, EventTime end, DateTimeOffset from, DateTimeOffset to)
    {
        var s = start.EarliestUtc;
        var e = end.LatestUtc;
        return s < to && (e > from || (s == e && s >= from));
    }

    private static EventTime ToEventTime(CalDateTime value, TimeZoneInfo? zone, string? zoneId)
    {
        var utc = new DateTimeOffset(DateTime.SpecifyKind(value.AsUtc, DateTimeKind.Utc));
        return EventTime.At(zone is null ? utc : TimeZoneInfo.ConvertTime(utc, zone), zoneId);
    }

    private static TimeZoneInfo? FindZone(string? id)
    {
        if (id is null)
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>Writes the series as a one-event iCalendar document Ical.Net can evaluate.</summary>
    internal static string ToICalendar(CalendarEvent series)
    {
        var ics = new StringBuilder();
        ics.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Dayflower//EN\r\nBEGIN:VEVENT\r\n");
        ics.Append("UID:").Append(series.Key.EventId).Append("\r\n");

        if (series.Start.IsAllDay)
        {
            ics.Append("DTSTART;VALUE=DATE:").Append(FormatDate(series.Start.Date!.Value)).Append("\r\n");
            ics.Append("DTEND;VALUE=DATE:").Append(FormatDate(series.End.Date!.Value)).Append("\r\n");
        }
        else
        {
            AppendDateTime(ics, "DTSTART", series.Start.DateTime!.Value, series.Start.TimeZone);
            AppendDateTime(ics, "DTEND", series.End.DateTime!.Value, series.End.TimeZone ?? series.Start.TimeZone);
        }

        foreach (var line in series.Recurrence)
        {
            ics.Append(line.Trim()).Append("\r\n");
        }

        ics.Append("END:VEVENT\r\nEND:VCALENDAR\r\n");
        return ics.ToString();
    }

    private static void AppendDateTime(StringBuilder ics, string name, DateTimeOffset value, string? zoneId)
    {
        var zone = FindZone(zoneId);
        if (zone is null)
        {
            ics.Append(name).Append(':')
               .Append(value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)).Append("\r\n");
            return;
        }

        var local = TimeZoneInfo.ConvertTime(value, zone).DateTime;
        ics.Append(name).Append(";TZID=").Append(zoneId).Append(':')
           .Append(local.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture)).Append("\r\n");
    }

    private static string FormatDate(DateOnly date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
}

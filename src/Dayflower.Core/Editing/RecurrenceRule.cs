// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using System.Text;
using Dayflower.Core.Model;

namespace Dayflower.Core.Editing;

public enum RepeatFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

/// <summary>
/// A repeat rule the editor can show and change: every N days/weeks/months/years, on chosen weekdays, on a day of the
/// month or a weekday of the month (e.g. the last Friday), ending never, on a date or after a number of times.
/// Rules Google can make but this can't describe are kept as they are.
/// </summary>
public sealed record RecurrenceRule
{
    private static readonly string[] DayCodes = ["SU", "MO", "TU", "WE", "TH", "FR", "SA"];

    public RepeatFrequency Frequency { get; init; }

    public int Interval { get; init; } = 1;

    /// <summary>For weekly rules: the days; empty means the start's weekday.</summary>
    public IReadOnlyList<DayOfWeek> Days { get; init; } = [];

    /// <summary>For monthly rules: repeat on the Nth weekday (1–4, or -1 for the last) instead of the day of the month.</summary>
    public int? WeekOfMonth { get; init; }

    public int? Count { get; init; }

    /// <summary>The last day it can occur on.</summary>
    public DateOnly? Until { get; init; }

    /// <summary>Reads an RRULE line, or returns null for rules this model can't represent.</summary>
    public static RecurrenceRule? Parse(string line, EventTime start)
    {
        string body = line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase) ? line[6..] : line;
        var parts = body.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0].ToUpperInvariant(), p => p[1].ToUpperInvariant());

        if (!parts.TryGetValue("FREQ", out var freq))
        {
            return null;
        }

        var frequency = freq switch
        {
            "DAILY" => RepeatFrequency.Daily,
            "WEEKLY" => RepeatFrequency.Weekly,
            "MONTHLY" => RepeatFrequency.Monthly,
            "YEARLY" => RepeatFrequency.Yearly,
            _ => (RepeatFrequency?)null,
        };
        if (frequency is null || parts.Keys.Except(["FREQ", "INTERVAL", "BYDAY", "COUNT", "UNTIL", "WKST", "BYMONTHDAY", "BYMONTH"]).Any())
        {
            return null;
        }

        var rule = new RecurrenceRule { Frequency = frequency.Value };
        if (parts.TryGetValue("INTERVAL", out var interval))
        {
            if (!int.TryParse(interval, CultureInfo.InvariantCulture, out int n) || n < 1)
            {
                return null;
            }

            rule = rule with { Interval = n };
        }

        var startDate = StartDate(start);
        if (parts.TryGetValue("BYDAY", out var byDay))
        {
            if (frequency == RepeatFrequency.Weekly)
            {
                var days = byDay.Split(',').Select(d => Array.IndexOf(DayCodes, d)).ToList();
                if (days.Any(d => d < 0))
                {
                    return null;
                }

                rule = rule with { Days = days.Select(d => (DayOfWeek)d).OrderBy(d => ((int)d + 6) % 7).ToList() };
            }
            else if (frequency == RepeatFrequency.Monthly && byDay.Length >= 3
                     && int.TryParse(byDay[..^2], CultureInfo.InvariantCulture, out int week) && week is >= -1 and <= 4 and not 0
                     && Array.IndexOf(DayCodes, byDay[^2..]) == (int)startDate.DayOfWeek)
            {
                rule = rule with { WeekOfMonth = week };
            }
            else
            {
                return null;
            }
        }

        // BYMONTHDAY/BYMONTH that only repeat the start's own day or month carry no extra meaning.
        if (parts.TryGetValue("BYMONTHDAY", out var monthDay) && monthDay != startDate.Day.ToString(CultureInfo.InvariantCulture))
        {
            return null;
        }

        if (parts.TryGetValue("BYMONTH", out var month) && month != startDate.Month.ToString(CultureInfo.InvariantCulture))
        {
            return null;
        }

        if (parts.TryGetValue("COUNT", out var count))
        {
            if (!int.TryParse(count, CultureInfo.InvariantCulture, out int c) || c < 1)
            {
                return null;
            }

            rule = rule with { Count = c };
        }

        if (parts.TryGetValue("UNTIL", out var until))
        {
            var date = ParseUntil(until, start);
            if (date is null)
            {
                return null;
            }

            rule = rule with { Until = date };
        }

        return rule;
    }

    /// <summary>The RRULE line for an event starting at <paramref name="start"/>.</summary>
    public string ToRRule(EventTime start)
    {
        var startDate = StartDate(start);
        var text = new StringBuilder("RRULE:FREQ=").Append(Frequency.ToString().ToUpperInvariant());
        if (Interval > 1)
        {
            text.Append(";INTERVAL=").Append(Interval.ToString(CultureInfo.InvariantCulture));
        }

        if (Frequency == RepeatFrequency.Weekly)
        {
            var days = Days.Count > 0 ? Days : [startDate.DayOfWeek];
            text.Append(";BYDAY=").AppendJoin(',', days.Distinct().OrderBy(d => ((int)d + 6) % 7).Select(d => DayCodes[(int)d]));
        }
        else if (Frequency == RepeatFrequency.Monthly && WeekOfMonth is { } week)
        {
            text.Append(";BYDAY=").Append(week.ToString(CultureInfo.InvariantCulture)).Append(DayCodes[(int)startDate.DayOfWeek]);
        }

        if (Count is { } count)
        {
            text.Append(";COUNT=").Append(count.ToString(CultureInfo.InvariantCulture));
        }
        else if (Until is { } until)
        {
            text.Append(";UNTIL=").Append(FormatUntil(until, start));
        }

        return text.ToString();
    }

    /// <summary>"Weekly on Monday and Wednesday", "Every 2 months on the last Friday, 10 times".</summary>
    public string Describe(EventTime start, CultureInfo names)
    {
        var startDate = StartDate(start);
        string unit = Frequency switch
        {
            RepeatFrequency.Daily => "day",
            RepeatFrequency.Weekly => "week",
            RepeatFrequency.Monthly => "month",
            _ => "year",
        };
        var text = new StringBuilder(Interval == 1
            ? Frequency switch
            {
                RepeatFrequency.Daily => "Daily",
                RepeatFrequency.Weekly => "Weekly",
                RepeatFrequency.Monthly => "Monthly",
                _ => "Annually",
            }
            : $"Every {Interval} {unit}s");

        switch (Frequency)
        {
            case RepeatFrequency.Weekly:
                var days = (Days.Count > 0 ? Days : [startDate.DayOfWeek]).Distinct().OrderBy(d => ((int)d + 6) % 7).ToList();
                text.Append(days.Count == 5 && !days.Contains(DayOfWeek.Saturday) && !days.Contains(DayOfWeek.Sunday)
                    ? " on weekdays"
                    : " on " + JoinWithAnd(days.Select(d => names.DateTimeFormat.GetDayName(d)).ToList()));
                break;
            case RepeatFrequency.Monthly:
                text.Append(WeekOfMonth is { } week
                    ? $" on the {(week == -1 ? "last" : Ordinal(week))} {names.DateTimeFormat.GetDayName(startDate.DayOfWeek)}"
                    : $" on day {startDate.Day}");
                break;
            case RepeatFrequency.Yearly:
                text.Append(" on ").Append(startDate.ToString("d MMMM", names));
                break;
        }

        if (Count is { } count)
        {
            text.Append(count == 1 ? ", once" : $", {count} times");
        }
        else if (Until is { } until)
        {
            text.Append(", until ").Append(until.ToString(until.Year == startDate.Year ? "d MMM" : "d MMM yyyy", names));
        }

        return text.ToString();
    }

    private static DateOnly StartDate(EventTime start) => start.Date ?? DateOnly.FromDateTime(LocalTime(start));

    private static DateTime LocalTime(EventTime start)
    {
        var dto = start.DateTime!.Value;
        if (start.TimeZone is { } zoneId)
        {
            try
            {
                return TimeZoneInfo.ConvertTime(dto, TimeZoneInfo.FindSystemTimeZoneById(zoneId)).DateTime;
            }
            catch (TimeZoneNotFoundException)
            {
            }
        }

        return dto.DateTime;
    }

    /// <summary>UNTIL as Google writes it: a date for all-day series, the end of that day in UTC for timed ones.</summary>
    internal static string FormatUntil(DateOnly until, EventTime start)
    {
        if (start.IsAllDay)
        {
            return until.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        var endOfDay = until.ToDateTime(new TimeOnly(23, 59, 59));
        DateTime utc;
        try
        {
            var zone = start.TimeZone is { } id ? TimeZoneInfo.FindSystemTimeZoneById(id) : null;
            utc = zone is null ? new DateTimeOffset(endOfDay, start.DateTime!.Value.Offset).UtcDateTime : TimeZoneInfo.ConvertTimeToUtc(endOfDay, zone);
        }
        catch (TimeZoneNotFoundException)
        {
            utc = new DateTimeOffset(endOfDay, start.DateTime!.Value.Offset).UtcDateTime;
        }

        return utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }

    private static DateOnly? ParseUntil(string value, EventTime start)
    {
        if (value.Length == 8 && DateOnly.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        if (DateTime.TryParseExact(value, ["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss"], CultureInfo.InvariantCulture,
                                   DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var moment))
        {
            var utc = new DateTimeOffset(DateTime.SpecifyKind(moment, DateTimeKind.Utc));
            return DateOnly.FromDateTime(LocalTime(EventTime.At(utc, start.TimeZone)));
        }

        return null;
    }

    private static string Ordinal(int n) => n switch { 1 => "first", 2 => "second", 3 => "third", _ => "fourth" };

    private static string JoinWithAnd(IReadOnlyList<string> items) =>
        items.Count <= 1 ? string.Join(string.Empty, items) : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
}

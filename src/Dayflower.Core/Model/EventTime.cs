// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

namespace Dayflower.Core.Model;

/// <summary>
/// When an event starts or ends: either a whole day (<see cref="Date"/>) or a moment (<see cref="DateTime"/>),
/// optionally with the IANA time zone the event was created in (needed to repeat it correctly across daylight
/// saving changes).
/// </summary>
public sealed record EventTime
{
    private EventTime(DateOnly? date, DateTimeOffset? dateTime, string? timeZone)
    {
        Date = date;
        DateTime = dateTime;
        TimeZone = timeZone;
    }

    /// <summary>Set for all-day events. An all-day end date is exclusive, as in iCalendar and Google Calendar.</summary>
    public DateOnly? Date { get; }

    /// <summary>Set for timed events.</summary>
    public DateTimeOffset? DateTime { get; }

    /// <summary>IANA time zone such as "Europe/Warsaw", when known.</summary>
    public string? TimeZone { get; }

    public bool IsAllDay => Date is not null;

    public static EventTime AllDay(DateOnly date) => new(date, null, null);

    public static EventTime At(DateTimeOffset dateTime, string? timeZone = null) => new(null, dateTime, timeZone);

    /// <summary>
    /// The earliest moment this time could mean anywhere in the world. A whole day starts at midnight in the
    /// viewer's time zone, up to 14 hours either side of midnight UTC, so for range queries all-day times are
    /// widened by 14 hours each way and filtered precisely later.
    /// </summary>
    public DateTimeOffset EarliestUtc => Date is { } d
        ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(-14)
        : DateTime!.Value.ToUniversalTime();

    /// <summary>The latest moment this time could mean anywhere in the world; see <see cref="EarliestUtc"/>.</summary>
    public DateTimeOffset LatestUtc => Date is { } d
        ? new DateTimeOffset(d.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddHours(14)
        : DateTime!.Value.ToUniversalTime();

    /// <summary>
    /// Identifies one occurrence of a repeating event: the date for all-day events, the UTC instant otherwise.
    /// Two times that mean the same occurrence produce the same key, whatever offset they were written with.
    /// </summary>
    public string OccurrenceKey => Date is { } d
        ? d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        : DateTime!.Value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

    public override string ToString() => Date is { } d
        ? d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        : DateTime!.Value.ToString("yyyy-MM-ddTHH:mm:sszzz", System.Globalization.CultureInfo.InvariantCulture)
          + (TimeZone is null ? string.Empty : $" [{TimeZone}]");
}

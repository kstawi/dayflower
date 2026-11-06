// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using System.Text.RegularExpressions;
using Dayflower.Core.Model;
using Dayflower.Core.Recurrence;

namespace Dayflower.Core.Editing;

/// <summary>
/// Splitting a repeating event for "this and following" changes, as Google Calendar does: the original series ends
/// just before the chosen occurrence, and a new series takes over from it.
/// </summary>
public static partial class SeriesSplit
{
    /// <summary>The series' recurrence lines, ending just before <paramref name="occurrenceStart"/>.</summary>
    public static IReadOnlyList<string> EndBefore(IReadOnlyList<string> recurrence, EventTime occurrenceStart)
    {
        string until = occurrenceStart.IsAllDay
            ? occurrenceStart.Date!.Value.AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            : occurrenceStart.DateTime!.Value.UtcDateTime.AddSeconds(-1).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        return recurrence.Select(line => IsRule(line) ? WithUntil(WithoutPart(line, "COUNT"), until) : line).ToList();
    }

    /// <summary>
    /// The recurrence for the new series that starts at the chosen occurrence: the same rule, with any COUNT reduced
    /// by the <paramref name="occurrencesBefore"/> the original series keeps.
    /// </summary>
    public static IReadOnlyList<string> ContinueAfter(IReadOnlyList<string> recurrence, int occurrencesBefore) =>
        recurrence.Select(line =>
        {
            if (!IsRule(line) || CountPart().Match(line) is not { Success: true } match)
            {
                return line;
            }

            int remaining = Math.Max(1, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) - occurrencesBefore);
            return CountPart().Replace(line, $"COUNT={remaining}");
        }).ToList();

    /// <summary>How many occurrences of <paramref name="series"/> come before <paramref name="occurrenceStart"/>.</summary>
    public static int OccurrencesBefore(CalendarEvent series, EventTime occurrenceStart)
    {
        var from = series.Start.EarliestUtc.AddDays(-1);
        var to = occurrenceStart.EarliestUtc;
        return to <= from ? 0 : RecurrenceExpander.Expand(series, from, to).Count(o => o.Start.EarliestUtc < occurrenceStart.EarliestUtc);
    }

    private static bool IsRule(string line) => line.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase);

    private static string WithoutPart(string line, string name) =>
        Regex.Replace(line, $";{name}=[^;]*", string.Empty, RegexOptions.IgnoreCase);

    private static string WithUntil(string line, string until) =>
        UntilPart().IsMatch(line) ? UntilPart().Replace(line, $"UNTIL={until}") : $"{line};UNTIL={until}";

    [GeneratedRegex("COUNT=(\\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex CountPart();

    [GeneratedRegex("UNTIL=[^;]*", RegexOptions.IgnoreCase)]
    private static partial Regex UntilPart();
}

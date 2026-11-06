// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

namespace Dayflower.Core.Calendar;

/// <summary>
/// The dates one calendar view shows, from <see cref="Start"/> (inclusive) to <see cref="End"/> (exclusive).
/// </summary>
/// <remarks>
/// <see cref="Anchor"/> is the date the user is on. Switching views keeps it, so going from month to week shows
/// the week that contains it, and moving forward or back moves it by one view's length.
/// </remarks>
public sealed record CalendarPeriod
{
    /// <summary>How many days the agenda list covers at a time.</summary>
    public const int AgendaDays = 30;

    public CalendarPeriod(CalendarViewKind kind, DateOnly anchor, DayOfWeek firstDayOfWeek)
    {
        Kind = kind;
        Anchor = anchor;
        FirstDayOfWeek = firstDayOfWeek;
        (Start, End) = kind switch
        {
            CalendarViewKind.Day => (anchor, anchor.AddDays(1)),
            CalendarViewKind.Week => WeekOf(anchor, firstDayOfWeek),
            CalendarViewKind.Month => MonthOf(anchor),
            CalendarViewKind.Agenda => (anchor, anchor.AddDays(AgendaDays)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    public CalendarViewKind Kind { get; }

    public DateOnly Anchor { get; }

    public DayOfWeek FirstDayOfWeek { get; }

    public DateOnly Start { get; }

    /// <summary>The day after the last day shown.</summary>
    public DateOnly End { get; }

    /// <summary>
    /// The first day on screen. The month view shows whole weeks, so it starts on the first day of the week that
    /// contains the 1st; other views start at <see cref="Start"/>.
    /// </summary>
    public DateOnly GridStart => Kind == CalendarViewKind.Month ? StartOfWeek(Start, FirstDayOfWeek) : Start;

    /// <summary>The day after the last day on screen; see <see cref="GridStart"/>.</summary>
    public DateOnly GridEnd => Kind == CalendarViewKind.Month ? StartOfWeek(End.AddDays(-1), FirstDayOfWeek).AddDays(7) : End;

    public bool Contains(DateOnly date) => date >= Start && date < End;

    public CalendarPeriod Next() => WithAnchor(Step(+1));

    public CalendarPeriod Previous() => WithAnchor(Step(-1));

    public CalendarPeriod WithAnchor(DateOnly anchor) => new(Kind, anchor, FirstDayOfWeek);

    public CalendarPeriod WithKind(CalendarViewKind kind) => new(kind, Anchor, FirstDayOfWeek);

    /// <summary>The first day of the week that contains <paramref name="date"/>.</summary>
    public static DateOnly StartOfWeek(DateOnly date, DayOfWeek firstDayOfWeek)
    {
        int daysIntoWeek = ((int)date.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        return date.AddDays(-daysIntoWeek);
    }

    private DateOnly Step(int direction) => Kind switch
    {
        CalendarViewKind.Day => Anchor.AddDays(direction),
        CalendarViewKind.Week => Anchor.AddDays(7 * direction),
        CalendarViewKind.Month => Anchor.AddMonths(direction),
        CalendarViewKind.Agenda => Anchor.AddDays(AgendaDays * direction),
        _ => throw new InvalidOperationException($"Unknown view kind {Kind}."),
    };

    private static (DateOnly Start, DateOnly End) WeekOf(DateOnly date, DayOfWeek firstDayOfWeek)
    {
        var start = StartOfWeek(date, firstDayOfWeek);
        return (start, start.AddDays(7));
    }

    private static (DateOnly Start, DateOnly End) MonthOf(DateOnly date)
    {
        var start = new DateOnly(date.Year, date.Month, 1);
        return (start, start.AddMonths(1));
    }
}

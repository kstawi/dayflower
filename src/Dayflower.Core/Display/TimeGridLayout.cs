// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

namespace Dayflower.Core.Display;

/// <summary>An event in one day of the hour grid, in minutes from midnight.</summary>
public readonly record struct TimeSlot(int Index, double StartMinutes, double EndMinutes);

/// <summary>Where a slot goes: which of the <see cref="Columns"/> side-by-side columns it takes.</summary>
public readonly record struct SlotPlacement(int Index, int Column, int Columns);

/// <summary>
/// Lays out clashing events side by side, as calendar apps do: events that overlap (directly or through a chain of
/// overlaps) form a group, each gets the leftmost free column, and the group shares the day's width equally.
/// </summary>
public static class TimeGridLayout
{
    /// <param name="minimumMinutes">
    /// The shortest an event is drawn, so a 5-minute event still has room for its title; overlap is judged on the
    /// drawn size.
    /// </param>
    public static IReadOnlyList<SlotPlacement> Arrange(IReadOnlyList<TimeSlot> slots, double minimumMinutes = 20)
    {
        var ordered = slots
            .OrderBy(s => s.StartMinutes)
            .ThenByDescending(s => s.EndMinutes - s.StartMinutes)
            .ToList();

        var result = new List<SlotPlacement>(slots.Count);
        var group = new List<(TimeSlot Slot, int Column)>();
        var columnEnds = new List<double>();
        double groupEnd = double.MinValue;

        foreach (var slot in ordered)
        {
            double end = Math.Max(slot.EndMinutes, slot.StartMinutes + minimumMinutes);
            if (group.Count > 0 && slot.StartMinutes >= groupEnd)
            {
                Flush();
            }

            int column = columnEnds.FindIndex(e => e <= slot.StartMinutes);
            if (column < 0)
            {
                column = columnEnds.Count;
                columnEnds.Add(end);
            }
            else
            {
                columnEnds[column] = end;
            }

            group.Add((slot, column));
            groupEnd = Math.Max(groupEnd, end);
        }

        Flush();
        return result;

        void Flush()
        {
            int columns = columnEnds.Count;
            result.AddRange(group.Select(g => new SlotPlacement(g.Slot.Index, g.Column, columns)));
            group.Clear();
            columnEnds.Clear();
            groupEnd = double.MinValue;
        }
    }
}

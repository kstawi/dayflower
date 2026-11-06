// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

namespace Dayflower.Core.Display;

/// <summary>An item covering days <see cref="FirstDay"/> to <see cref="LastDay"/> (inclusive) of a row of days.</summary>
public readonly record struct DaySpan(int Index, int FirstDay, int LastDay);

public readonly record struct LanePlacement(int Index, int Lane);

/// <summary>
/// Stacks day-spanning items (the all-day area, a week row of the month view) into lanes: each item goes into the
/// first lane where it fits, in the order given, so callers decide what gets the top lanes.
/// </summary>
public static class LaneLayout
{
    public static IReadOnlyList<LanePlacement> Arrange(IReadOnlyList<DaySpan> spans)
    {
        var lanes = new List<List<(int First, int Last)>>();
        var result = new List<LanePlacement>(spans.Count);

        foreach (var span in spans)
        {
            int lane = lanes.FindIndex(l => l.All(taken => span.LastDay < taken.First || span.FirstDay > taken.Last));
            if (lane < 0)
            {
                lane = lanes.Count;
                lanes.Add([]);
            }

            lanes[lane].Add((span.FirstDay, span.LastDay));
            result.Add(new LanePlacement(span.Index, lane));
        }

        return result;
    }
}

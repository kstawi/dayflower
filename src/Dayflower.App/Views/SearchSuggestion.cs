// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Calendar;
using Microsoft.UI.Xaml.Media;

namespace Dayflower.Views;

/// <summary>A search result as the search box's suggestion list shows it: colour, title and when.</summary>
public sealed class SearchSuggestion(CalendarItem item)
{
    public CalendarItem Item { get; } = item;

    public string Title => Item.Title;

    public string When { get; } = TimeText.When(item).Replace('\n', ' ');

    public SolidColorBrush ColorBrush { get; } = new(item.Color);

    public override string ToString() => $"{Title}, {When}";
}

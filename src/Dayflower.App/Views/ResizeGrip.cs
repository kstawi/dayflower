// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Dayflower.Views;

/// <summary>The bottom edge of an event in the time grid: drag it to change when the event ends.</summary>
internal sealed partial class ResizeGrip : Grid
{
    public ResizeGrip()
    {
        Height = 6;
        VerticalAlignment = VerticalAlignment.Bottom;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeNorthSouth);
    }
}

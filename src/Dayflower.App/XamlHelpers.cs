// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Microsoft.UI.Xaml.Media;

namespace Dayflower;

/// <summary>Small conversions for x:Bind function bindings.</summary>
public static class XamlHelpers
{
    public static SolidColorBrush Brush(Windows.UI.Color color) => new(color);
}

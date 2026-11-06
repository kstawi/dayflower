// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Calendar;
using Dayflower.Core.Model;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Dayflower.Views;

/// <summary>Raised when the user opens an item: the element to anchor its details to, and the item.</summary>
public sealed class ItemClickedEventArgs(FrameworkElement anchor, CalendarItem item) : EventArgs
{
    public FrameworkElement Anchor { get; } = anchor;

    public CalendarItem Item { get; } = item;
}

/// <summary>
/// How events look in every view: a tint of the calendar's colour with a solid bar on the left, like Windows'
/// own calendar surfaces. Unanswered invitations are outlined instead of filled, tentative ones are fainter.
/// </summary>
internal static class EventVisuals
{
    private const byte FillAlpha = 0x55;
    private const byte FaintFillAlpha = 0x22;

    /// <summary>A block for the hour grid or the all-day area.</summary>
    public static Button Block(CalendarItem item, string? timeText, bool singleLine, ElementTheme theme, Action<FrameworkElement, CalendarItem> onClick, double rightInset = 0)
    {
        var text = new StackPanel { Spacing = 0, Padding = new Thickness(6, singleLine ? 1 : 3, 4 + rightInset, 2) };
        text.Children.Add(new TextBlock
        {
            Text = singleLine && timeText is not null ? $"{item.Title}, {timeText}" : item.Title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = singleLine ? TextWrapping.NoWrap : TextWrapping.Wrap,
            MaxLines = singleLine ? 1 : 3,
            FontStyle = item.MyResponse == AttendeeResponse.Tentative ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
        });
        if (!singleLine && timeText is not null)
        {
            text.Children.Add(new TextBlock
            {
                Text = timeText,
                FontSize = 11,
                Foreground = ThemeResource<Brush>("TextFillColorSecondaryBrush", theme),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap,
            });
        }

        return Wrap(item, Surface(item, text, theme), timeText, onClick);
    }

    /// <summary>A one-line chip for the month view: a filled bar for all-day items, "• 13:30 Title" for timed ones.</summary>
    public static Button Chip(CalendarItem item, bool allDay, ElementTheme theme, Action<FrameworkElement, CalendarItem> onClick)
    {
        if (allDay)
        {
            var label = new TextBlock
            {
                Text = item.Title,
                FontSize = 12,
                Padding = new Thickness(6, 1, 4, 1),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return Wrap(item, Surface(item, label, theme), null, onClick);
        }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Padding = new Thickness(4, 0, 4, 0) };
        row.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = item.MyResponse == AttendeeResponse.NeedsAction ? null : new SolidColorBrush(item.Color),
            Stroke = new SolidColorBrush(item.Color),
            StrokeThickness = 1.5,
        });
        row.Children.Add(new TextBlock
        {
            Text = TimeText.Time(item.Span.Start),
            FontSize = 12,
            Foreground = ThemeResource<Brush>("TextFillColorSecondaryBrush", theme),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return Wrap(item, row, TimeText.Time(item.Span.Start), onClick);
    }

    public static SolidColorBrush Tint(Color color, byte alpha) => new(Color.FromArgb(alpha, color.R, color.G, color.B));

    /// <summary>A resource that doesn't depend on the theme, such as a style.</summary>
    public static T Resource<T>(string key) => (T)Application.Current.Resources[key];

    /// <summary>
    /// A theme resource (brush) for <paramref name="theme"/>. Application.Current.Resources only knows the theme the
    /// app started in, so elements built in code look up the light or dark dictionary themselves; otherwise a view
    /// in light mode (or after Windows switches theme) gets dark-theme brushes.
    /// </summary>
    public static T ThemeResource<T>(string key, ElementTheme theme)
    {
        string themeKey = theme == ElementTheme.Light ? "Light" : "Dark";
        return TryFind(Application.Current.Resources, key, themeKey, out object? value)
            ? (T)value!
            : (T)Application.Current.Resources[key];
    }

    private static bool TryFind(ResourceDictionary dictionary, string key, string themeKey, out object? value)
    {
        if ((dictionary.ThemeDictionaries.TryGetValue(themeKey, out object? themed)
             || (themeKey == "Dark" && dictionary.ThemeDictionaries.TryGetValue("Default", out themed)))
            && themed is ResourceDictionary themeDictionary
            && themeDictionary.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (TryFind(merged, key, themeKey, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    private static Grid Surface(CalendarItem item, UIElement content, ElementTheme theme)
    {
        bool unanswered = item.MyResponse == AttendeeResponse.NeedsAction;
        bool tentative = item.MyResponse == AttendeeResponse.Tentative;
        var surface = new Grid
        {
            CornerRadius = new CornerRadius(4),
            Background = unanswered ? ThemeResource<Brush>("LayerFillColorDefaultBrush", theme) : Tint(item.Color, tentative ? FaintFillAlpha : FillAlpha),
            BorderBrush = unanswered || tentative ? new SolidColorBrush(item.Color) : null,
            BorderThickness = unanswered || tentative ? new Thickness(1) : new Thickness(0),
        };
        surface.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        surface.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        surface.Children.Add(new Border { Background = new SolidColorBrush(item.Color) });
        Grid.SetColumn((FrameworkElement)content, 1);
        surface.Children.Add(content);
        return surface;
    }

    private static Button Wrap(CalendarItem item, UIElement content, string? timeText, Action<FrameworkElement, CalendarItem> onClick)
    {
        var button = new Button
        {
            Style = Resource<Style>("EventBlockButtonStyle"),
            Content = content,
        };
        AutomationProperties.SetName(button, timeText is null ? item.Title : $"{item.Title}, {timeText}");
        ToolTipService.SetToolTip(button, $"{item.Title}\n{TimeText.When(item)}");
        button.Click += (_, _) => onClick(button, item);
        return button;
    }
}

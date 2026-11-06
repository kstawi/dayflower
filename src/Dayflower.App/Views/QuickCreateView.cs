// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Dayflower.Views;

/// <summary>
/// The flyout for a new event over empty time: a title, the time picked, the calendar, and Save or More options
/// (the full editor).
/// </summary>
public sealed partial class QuickCreateView : UserControl
{
    private readonly TextBox _title = new() { PlaceholderText = "Add title", FontSize = 18, MaxLength = 1024 };
    private readonly ComboBox _calendar = new() { HorizontalAlignment = HorizontalAlignment.Stretch };

    public QuickCreateView(
        SlotSelectedEventArgs slot, IReadOnlyList<EditorCalendar> calendars, EditorCalendar? selected, ElementTheme theme,
        Func<string, EditorCalendar, Task> save, Func<string, EditorCalendar, Task> moreOptions)
    {
        var root = new StackPanel { Spacing = 12, Width = 340 };
        root.Children.Add(_title);

        var day = DateOnly.FromDateTime(slot.Start);
        string when = slot.AllDay
            ? TimeText.LongDate(day) + (slot.End.Date > slot.Start.Date.AddDays(1) ? $" – {TimeText.LongDate(DateOnly.FromDateTime(slot.End).AddDays(-1))}" : string.Empty) + " · All day"
            : $"{TimeText.LongDate(day)} · {TimeText.TimeRange(slot.Start, slot.End)}";
        root.Children.Add(new TextBlock
        {
            Text = when,
            TextWrapping = TextWrapping.Wrap,
            Foreground = EventVisuals.ThemeResource<Microsoft.UI.Xaml.Media.Brush>("TextFillColorSecondaryBrush", theme),
        });

        foreach (var calendar in calendars)
        {
            _calendar.Items.Add(calendar);
        }

        _calendar.SelectedItem = selected ?? calendars[0];
        if (calendars.Count > 1)
        {
            root.Children.Add(_calendar);
        }

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var more = new Button { Content = "More options" };
        var saveButton = new Button { Content = "Save", Style = EventVisuals.Resource<Style>("AccentButtonStyle") };
        buttons.Children.Add(more);
        buttons.Children.Add(saveButton);
        root.Children.Add(buttons);

        EditorCalendar Calendar() => (EditorCalendar)(_calendar.SelectedItem ?? calendars[0]);
        saveButton.Click += async (_, _) => await save(_title.Text.Trim(), Calendar());
        more.Click += async (_, _) => await moreOptions(_title.Text.Trim(), Calendar());
        _title.KeyDown += async (_, e) =>
        {
            if (e.Key == VirtualKey.Enter)
            {
                e.Handled = true;
                await save(_title.Text.Trim(), Calendar());
            }
        };
        Loaded += (_, _) => _title.Focus(FocusState.Programmatic);
        Content = root;
    }
}

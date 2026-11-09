// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Calendar;
using Dayflower.Core.Display;
using Dayflower.Core.Search;
using Dayflower.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Dayflower.Views;

/// <summary>A time zone to add, as the search lists it: a city, or a time zone by its Windows name.</summary>
public sealed record ZoneChoice(string Label, string Detail, string ZoneId)
{
    public override string ToString() => $"{Label} · {Detail}";
}

/// <summary>
/// The world clock in the side pane: one row per chosen time zone with the user's label, the time there and how far
/// ahead or behind it is ("Team NY 09:14 −6h"). Zones are added from a city search, and can be renamed, moved and
/// removed. Daylight saving is followed automatically.
/// </summary>
public sealed partial class WorldClockView : UserControl
{
    private readonly StackPanel _rows = new() { Spacing = 10 };
    private readonly TextBlock _hint = new()
    {
        Text = "Add the time zones of the people you work with.",
        Style = EventVisuals.Resource<Style>("PaneHintTextStyle"),
    };

    private readonly DispatcherQueueTimer _timer;
    private readonly List<(WorldClockEntry Entry, TextBlock Time, TextBlock Detail)> _clocks = [];
    private AppSettings? _settings;

    public WorldClockView()
    {
        var header = new Grid();
        var title = new TextBlock { Text = "Time zones", Style = EventVisuals.Resource<Style>("PaneHeaderTextStyle"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        header.Children.Add(title);
        var add = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 14 },
            Style = EventVisuals.Resource<Style>("SubtleIconButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        AutomationProperties.SetName(add, "Add time zone");
        ToolTipService.SetToolTip(add, "Add time zone");
        add.Click += (_, _) => ShowAddFlyout(add);
        header.Children.Add(add);

        var root = new StackPanel { Spacing = 8 };
        root.Children.Add(header);
        root.Children.Add(_hint);
        root.Children.Add(_rows);
        Content = root;

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(10);
        _timer.Tick += (_, _) => UpdateTimes();
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    public void Initialize(AppSettings settings)
    {
        _settings = settings;
        Build();
    }

    private List<WorldClockEntry> Entries => _settings?.WorldClocks ?? [];

    private void Build()
    {
        _rows.Children.Clear();
        _clocks.Clear();
        var entries = Entries;
        _hint.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < entries.Count; i++)
        {
            _rows.Children.Add(Row(entries[i], i));
        }

        UpdateTimes();
    }

    private Grid Row(WorldClockEntry entry, int index)
    {
        var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(4, 0, 0, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = entry.Label, TextTrimming = TextTrimming.CharacterEllipsis });
        var detail = new TextBlock { Style = EventVisuals.Resource<Style>("CaptionTextBlockStyle"), Foreground = Secondary(), TextTrimming = TextTrimming.CharacterEllipsis };
        names.Children.Add(detail);
        row.Children.Add(names);

        var time = new TextBlock { FontSize = 20, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(time, 1);
        row.Children.Add(time);

        var more = new Button
        {
            Content = new FontIcon { Glyph = "", FontSize = 12 },
            Style = EventVisuals.Resource<Style>("SubtleIconButtonStyle"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(more, $"Options for {entry.Label}");
        ToolTipService.SetToolTip(more, "Options");
        var menu = new MenuFlyout();
        menu.Items.Add(MenuItem("Rename", "", () => ShowRenameFlyout(more, entry)));
        if (index > 0)
        {
            menu.Items.Add(MenuItem("Move up", "", () => Move(entry, -1)));
        }

        if (index < Entries.Count - 1)
        {
            menu.Items.Add(MenuItem("Move down", "", () => Move(entry, 1)));
        }

        menu.Items.Add(MenuItem("Remove", "", () => Remove(entry)));
        more.Flyout = menu;
        Grid.SetColumn(more, 2);
        row.Children.Add(more);

        _clocks.Add((entry, time, detail));
        return row;
    }

    private void UpdateTimes()
    {
        var now = DateTimeOffset.Now;
        foreach (var (entry, time, detail) in _clocks)
        {
            var zone = TimeZones.Find(entry.ZoneId);
            if (zone is null)
            {
                time.Text = "–";
                detail.Text = "Unknown time zone";
                continue;
            }

            var reading = WorldClock.Read(now, zone, TimeZoneInfo.Local);
            time.Text = TimeText.Time(reading.Time);
            string difference = WorldClock.FormatDifference(reading.Difference);
            detail.Text = WorldClock.FormatDay(reading.DayOffset) is { } day ? $"{difference} · {day}" : difference;
            AutomationProperties.SetName(time, $"{entry.Label}: {time.Text}, {detail.Text}");
        }
    }

    private void Save()
    {
        _settings?.Save();
        Build();
    }

    private void Move(WorldClockEntry entry, int by)
    {
        var entries = Entries;
        int index = entries.IndexOf(entry);
        int target = Math.Clamp(index + by, 0, entries.Count - 1);
        entries.RemoveAt(index);
        entries.Insert(target, entry);
        Save();
    }

    private void Remove(WorldClockEntry entry)
    {
        Entries.Remove(entry);
        Save();
    }

    private void ShowRenameFlyout(FrameworkElement anchor, WorldClockEntry entry)
    {
        var box = new TextBox { Text = entry.Label, Header = "Name", MaxLength = 40, Width = 240 };
        var save = new Button { Content = "Save", Style = EventVisuals.Resource<Style>("AccentButtonStyle"), HorizontalAlignment = HorizontalAlignment.Right };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(box);
        panel.Children.Add(save);
        var flyout = new Flyout { Content = panel, XamlRoot = anchor.XamlRoot };

        void Apply()
        {
            if (box.Text.Trim() is { Length: > 0 } label)
            {
                entry.Label = label;
                flyout.Hide();
                Save();
            }
        }

        save.Click += (_, _) => Apply();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                Apply();
            }
        };
        flyout.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
        flyout.ShowAt(anchor);
    }

    private void ShowAddFlyout(FrameworkElement anchor)
    {
        var search = new AutoSuggestBox
        {
            PlaceholderText = "Search for a city or time zone",
            Width = 300,
            QueryIcon = new SymbolIcon(Symbol.Find),
            UpdateTextOnSelect = false,
        };
        AutomationProperties.SetName(search, "Search for a city or time zone");
        var flyout = new Flyout { Content = search, XamlRoot = anchor.XamlRoot };

        search.TextChanged += (sender, args) =>
        {
            if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                sender.ItemsSource = Find(sender.Text);
            }
        };
        search.QuerySubmitted += (sender, args) =>
        {
            var choice = args.ChosenSuggestion as ZoneChoice ?? Find(args.QueryText).FirstOrDefault();
            if (choice is null || _settings is null)
            {
                return;
            }

            Entries.Add(new WorldClockEntry { ZoneId = choice.ZoneId, Label = choice.Label });
            flyout.Hide();
            Save();
        };
        flyout.Opened += (_, _) => search.Focus(FocusState.Programmatic);
        flyout.ShowAt(anchor);
    }

    /// <summary>Cities first, then time zones whose Windows name matches ("Pacific", "Eastern", "UTC+05:30").</summary>
    private static List<ZoneChoice> Find(string query)
    {
        var results = Cities.Find(query).Select(c => new ZoneChoice(c.Name, c.Country, c.ZoneId)).ToList();
        var words = EventSearchIndex.Words(query);
        if (words.Count > 0)
        {
            foreach (var (id, name) in TimeZones.All)
            {
                string folded = EventSearchIndex.Fold(name + " " + id.Replace('_', ' '));
                if (words.All(w => folded.Contains(w, StringComparison.Ordinal)) && results.Count < 20)
                {
                    // "(UTC-05:00) Eastern Time (US & Canada)" is labelled "Eastern Time (US & Canada)".
                    int close = name.IndexOf(") ", StringComparison.Ordinal);
                    results.Add(new ZoneChoice(close > 0 ? name[(close + 2)..] : name, close > 0 ? name[1..close] : id, id));
                }
            }
        }

        return results;
    }

    private static MenuFlyoutItem MenuItem(string text, string glyph, Action action)
    {
        var item = new MenuFlyoutItem { Text = text, Icon = new FontIcon { Glyph = glyph } };
        item.Click += (_, _) => action();
        return item;
    }

    private Brush Secondary() => EventVisuals.ThemeResource<Brush>("TextFillColorSecondaryBrush", ActualTheme);
}

// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using System.Text;
using Dayflower.Calendar;
using Dayflower.Core.Display;
using Dayflower.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Dayflower.Views;

/// <summary>
/// The details of an event, shown in a flyout when it's clicked: when, where, which calendars, guests and their
/// answers, and the description. Read-only for now; editing, RSVP and Join come in later phases.
/// </summary>
public sealed partial class EventDetailsView : UserControl
{
    private const int MaxGuestsListed = 12;

    private readonly ElementTheme _theme;

    public EventDetailsView(CalendarItem item, ElementTheme theme, Func<Uri, Task> openLink)
    {
        _theme = theme;
        var e = item.Event;
        var root = new StackPanel { Spacing = 14, Width = 360 };

        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.Children.Add(new Border
        {
            Width = 14,
            Height = 14,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(item.Color),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(2, 7, 0, 0),
        });
        var title = new TextBlock
        {
            Text = item.Title,
            Style = EventVisuals.Resource<Style>("SubtitleTextBlockStyle"),
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };
        Grid.SetColumn(title, 1);
        heading.Children.Add(title);
        root.Children.Add(heading);

        root.Children.Add(Line("", TimeText.When(item) + (item.IsRepeating ? "\nRepeats" : string.Empty)));

        if (!string.IsNullOrWhiteSpace(e.Location))
        {
            root.Children.Add(Line("", e.Location.Trim()));
        }

        if (Meeting(e) is { } meeting)
        {
            root.Children.Add(Line("", meeting));
        }

        root.Children.Add(Line("", string.Join("\n", item.Copies.Select(Where))));

        if (item.MyResponse is { } response)
        {
            root.Children.Add(Line("", "Your answer: " + Answer(response)));
        }

        if (e.Attendees.Count > 0)
        {
            root.Children.Add(Line("", Guests(e)));
        }

        string description = HtmlText.ToPlainText(e.Description);
        if (description.Length > 0)
        {
            root.Children.Add(new ScrollViewer
            {
                MaxHeight = 220,
                Content = new TextBlock
                {
                    Text = description,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true,
                    Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                },
            });
        }

        if (Uri.TryCreate(e.WebLink, UriKind.Absolute, out var link))
        {
            var open = new HyperlinkButton { Content = "Open in Google Calendar", Padding = new Thickness(0), Margin = new Thickness(28, 0, 0, 0) };
            open.Click += async (_, _) => await openLink(link);
            root.Children.Add(open);
        }

        Content = root;
    }

    private static string Where(CalendarCopy copy) =>
        copy.CalendarName == copy.AccountEmail || copy.AccountEmail.Length == 0
            ? copy.CalendarName
            : $"{copy.CalendarName} · {copy.AccountEmail}";

    private static string? Meeting(CalendarEvent e)
    {
        var video = e.Conference?.EntryPoints.FirstOrDefault(p => p.Type == "video");
        if (video is not null)
        {
            return $"{e.Conference!.SolutionName ?? "Video call"} · {video.Label ?? video.Uri}";
        }

        return e.HangoutLink is { } meet ? $"Google Meet · {meet.Replace("https://", string.Empty, StringComparison.Ordinal)}" : null;
    }

    private static string Answer(AttendeeResponse response) => response switch
    {
        AttendeeResponse.Accepted => "going",
        AttendeeResponse.Tentative => "maybe",
        AttendeeResponse.Declined => "not going",
        _ => "not answered yet",
    };

    private static string Guests(CalendarEvent e)
    {
        var people = e.Attendees.Where(a => !a.IsResource).ToList();
        int yes = people.Count(a => a.Response == AttendeeResponse.Accepted);
        int maybe = people.Count(a => a.Response == AttendeeResponse.Tentative);
        int no = people.Count(a => a.Response == AttendeeResponse.Declined);
        int waiting = people.Count(a => a.Response == AttendeeResponse.NeedsAction);

        var text = new StringBuilder();
        text.Append(CultureInfo.CurrentCulture, $"{people.Count} {(people.Count == 1 ? "guest" : "guests")}");
        var parts = new List<string>();
        if (yes > 0) parts.Add($"{yes} yes");
        if (maybe > 0) parts.Add($"{maybe} maybe");
        if (no > 0) parts.Add($"{no} no");
        if (waiting > 0) parts.Add($"{waiting} awaiting");
        if (parts.Count > 0)
        {
            text.Append(" · ").AppendJoin(", ", parts);
        }

        foreach (var a in people.OrderByDescending(a => a.IsOrganizer).ThenBy(a => a.DisplayName ?? a.Email).Take(MaxGuestsListed))
        {
            string mark = a.Response switch
            {
                AttendeeResponse.Accepted => "✓",
                AttendeeResponse.Declined => "✕",
                AttendeeResponse.Tentative => "?",
                _ => "·",
            };
            text.Append('\n').Append(mark).Append(' ').Append(a.DisplayName ?? a.Email);
            if (a.IsOrganizer)
            {
                text.Append(" (organizer)");
            }
            else if (a.IsOptional)
            {
                text.Append(" (optional)");
            }
        }

        if (people.Count > MaxGuestsListed)
        {
            text.Append(CultureInfo.CurrentCulture, $"\nand {people.Count - MaxGuestsListed} more");
        }

        return text.ToString();
    }

    private Grid Line(string glyph, string text)
    {
        var line = new Grid { ColumnSpacing = 12 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 16,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
        });
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        Grid.SetColumn(block, 1);
        line.Children.Add(block);
        return line;
    }

    private Brush ThemeBrush(string key) => EventVisuals.ThemeResource<Brush>(key, _theme);
}

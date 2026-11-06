// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using System.Text;
using Dayflower.Calendar;
using Dayflower.Core.Display;
using Dayflower.Core.Meetings;
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

    /// <summary>Segoe Fluent Icons "Video".</summary>
    private static readonly string VideoGlyph = char.ConvertFromUtf32(0xE714);

    private readonly ElementTheme _theme;

    public EventDetailsView(CalendarItem item, ElementTheme theme, Func<Uri, Task> openLink, Func<CalendarItem, string?, Task>? join = null)
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

        if (item.Meeting is { } joinable && join is not null)
        {
            root.Children.Add(JoinButtons(item, joinable, join));
        }

        root.Children.Add(Line("", TimeText.When(item) + (item.IsRepeating ? "\nRepeats" : string.Empty)));

        if (!string.IsNullOrWhiteSpace(e.Location))
        {
            root.Children.Add(Line("", e.Location.Trim()));
        }

        if (item.Meeting is { } meeting)
        {
            root.Children.Add(Line(VideoGlyph, $"{meeting.ProviderName} · {meeting.DisplayText}"));
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

    /// <summary>
    /// The Join button, available at any time. For a Google Meet on several of the user's accounts, "Join as" links
    /// open it as each of the other accounts too.
    /// </summary>
    private static StackPanel JoinButtons(CalendarItem item, MeetingLink meeting, Func<CalendarItem, string?, Task> join)
    {
        var panel = new StackPanel { Spacing = 4, Margin = new Thickness(28, -4, 0, 0) };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(new FontIcon { Glyph = VideoGlyph, FontSize = 14 });
        content.Children.Add(new TextBlock { Text = meeting.JoinLabel });
        var button = new Button
        {
            Content = content,
            Style = EventVisuals.Resource<Style>("AccentButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"{meeting.JoinLabel}: {item.Title}");
        button.Click += async (_, _) => await join(item, null);
        panel.Children.Add(button);

        if (meeting.Provider == MeetingProvider.GoogleMeet && item.Accounts.Count > 1)
        {
            foreach (var account in item.Accounts.Skip(1))
            {
                var other = new HyperlinkButton { Content = $"Join as {account}", Padding = new Thickness(0) };
                other.Click += async (_, _) => await join(item, account);
                panel.Children.Add(other);
            }
        }

        return panel;
    }

    private static string Where(CalendarCopy copy) =>
        copy.CalendarName == copy.AccountEmail || copy.AccountEmail.Length == 0
            ? copy.CalendarName
            : $"{copy.CalendarName} · {copy.AccountEmail}";

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

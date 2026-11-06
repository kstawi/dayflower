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
/// answers, and the description; with Join, Edit and Delete, and Yes / Maybe / No for invitations.
/// </summary>
public sealed partial class EventDetailsView : UserControl
{
    private const int MaxGuestsListed = 12;

    /// <summary>Segoe Fluent Icons "Video".</summary>
    private static readonly string VideoGlyph = char.ConvertFromUtf32(0xE714);

    private readonly ElementTheme _theme;

    public EventDetailsView(
        CalendarItem item, ElementTheme theme, Func<Uri, Task> openLink, Func<CalendarItem, string?, Task>? join = null,
        EventActions? actions = null, Action? hide = null)
    {
        _theme = theme;
        hide ??= () => { };
        var e = item.Event;
        var root = new StackPanel { Spacing = 14, Width = 360 };

        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
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
        if (actions is not null && Tools(item, actions, hide) is { } tools)
        {
            Grid.SetColumn(tools, 2);
            heading.Children.Add(tools);
        }

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

        bool canReply = actions is not null && item.RespondableCopy is not null;
        if (item.MyResponse is { } response && !canReply)
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

        if (canReply)
        {
            root.Children.Add(Reply(item, actions!, hide));
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

    /// <summary>Edit and Delete (Decline for someone else's invitation), unavailable while offline.</summary>
    private static StackPanel? Tools(CalendarItem item, EventActions actions, Action hide)
    {
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Top };
        if (item.EditableCopy is not null)
        {
            tools.Children.Add(Tool("", "Edit event", hide, () => actions.EditAsync(item)));
        }

        if (item.DeletableCopy is not null)
        {
            tools.Children.Add(Tool("", "Delete event", hide, () => actions.DeleteAsync(item)));
        }
        else if (item.RespondableCopy is not null)
        {
            tools.Children.Add(Tool("", "Decline and remove", hide, () => actions.DeleteAsync(item)));
        }

        return tools.Children.Count > 0 ? tools : null;
    }

    private static Button Tool(string glyph, string name, Action hide, Func<Task> action)
    {
        bool online = EventActions.IsOnline;
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 16 },
            Style = EventVisuals.Resource<Style>("SubtleIconButtonStyle"),
            IsEnabled = online,
        };
        ToolTipService.SetToolTip(button, online ? name : $"{name} (needs an internet connection)");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, name);
        button.Click += async (_, _) =>
        {
            hide();
            await action();
        };
        return button;
    }

    /// <summary>"Going? Yes Maybe No", with the current answer highlighted, and a link to add a note.</summary>
    private Grid Reply(CalendarItem item, EventActions actions, Action hide)
    {
        var self = item.RespondableCopy!.Event.Self;
        bool online = EventActions.IsOnline;
        var panel = new StackPanel { Spacing = 6 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        buttons.Children.Add(new TextBlock { Text = "Going?", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        foreach (var (label, answer) in new[] { ("Yes", AttendeeResponse.Accepted), ("Maybe", AttendeeResponse.Tentative), ("No", AttendeeResponse.Declined) })
        {
            var button = new Button { Content = label, MinWidth = 60, IsEnabled = online };
            if (self?.Response == answer)
            {
                button.Style = EventVisuals.Resource<Style>("AccentButtonStyle");
            }

            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"Going: {label}");
            button.Click += async (_, _) =>
            {
                hide();
                await actions.RespondAsync(item, answer);
            };
            buttons.Children.Add(button);
        }

        panel.Children.Add(buttons);
        if (!string.IsNullOrWhiteSpace(self?.Comment))
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Your note: {self.Comment.Trim()}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            });
        }

        var note = new HyperlinkButton
        {
            Content = string.IsNullOrWhiteSpace(self?.Comment) ? "Add a note" : "Change note",
            Padding = new Thickness(0),
            IsEnabled = online,
        };
        note.Click += async (_, _) =>
        {
            hide();
            await actions.RespondWithNoteAsync(item);
        };
        panel.Children.Add(note);

        var line = new Grid { ColumnSpacing = 12 };
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        line.Children.Add(new FontIcon
        {
            Glyph = "",
            FontSize = 16,
            Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 8, 0, 0),
        });
        Grid.SetColumn(panel, 1);
        line.Children.Add(panel);
        return line;
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

// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Dayflower.Core.Editing;
using Dayflower.Core.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Dayflower.Views;

/// <summary>What a guest notification is about.</summary>
public enum GuestNotice
{
    Invitation,
    Update,
    Cancellation,
}

/// <summary>The small questions an edit may need answered: which occurrences, and whether to email guests.</summary>
public static class EditPrompts
{
    /// <summary>Asks which occurrences of a repeating event a change is for; null when the user backs out.</summary>
    public static async Task<EditScope?> AskScopeAsync(XamlRoot root, ElementTheme theme, string title, bool allowThis = true, bool allowFollowing = true)
    {
        var scopes = new List<(EditScope Scope, string Text)>();
        if (allowThis) scopes.Add((EditScope.ThisEvent, "This event"));
        if (allowFollowing) scopes.Add((EditScope.ThisAndFollowing, "This and following events"));
        scopes.Add((EditScope.AllEvents, "All events"));

        var options = new RadioButtons { SelectedIndex = 0 };
        foreach (var (_, text) in scopes)
        {
            options.Items.Add(text);
        }

        var dialog = Dialog(root, theme, title, options, "OK", cancel: "Cancel");
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? scopes[Math.Max(0, options.SelectedIndex)].Scope : null;
    }

    /// <summary>Asks whether to email the guests; null when the user goes back to editing.</summary>
    public static async Task<bool?> AskNotifyAsync(XamlRoot root, ElementTheme theme, GuestNotice notice)
    {
        string text = notice switch
        {
            GuestNotice.Invitation => "Send invitation emails to the guests?",
            GuestNotice.Cancellation => "Send cancellation emails to the guests?",
            _ => "Send update emails to the guests?",
        };
        var dialog = Dialog(root, theme, text, null, "Send", secondary: "Don't send", cancel: notice == GuestNotice.Cancellation ? "Cancel" : "Back");
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => true,
            ContentDialogResult.Secondary => false,
            _ => null,
        };
    }

    public static async Task<bool> ConfirmAsync(XamlRoot root, ElementTheme theme, string title, string text, string confirm)
    {
        var dialog = Dialog(root, theme, title, text, confirm, cancel: "Cancel");
        dialog.DefaultButton = ContentDialogButton.Close;
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Asks for an answer to an invitation with a note for the organiser; null when cancelled.</summary>
    public static async Task<Rsvp?> AskRsvpWithNoteAsync(XamlRoot root, ElementTheme theme, AttendeeResponse? current, string? comment)
    {
        var answers = new RadioButtons { Header = "Going?", MaxColumns = 3 };
        answers.Items.Add("Yes");
        answers.Items.Add("Maybe");
        answers.Items.Add("No");
        answers.SelectedIndex = current switch
        {
            AttendeeResponse.Tentative => 1,
            AttendeeResponse.Declined => 2,
            _ => 0,
        };
        var note = new TextBox
        {
            Header = "Note for the organizer",
            Text = comment ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            MaxLength = 1000,
        };
        var content = new StackPanel { Spacing = 16, MinWidth = 360 };
        content.Children.Add(answers);
        content.Children.Add(note);

        var dialog = Dialog(root, theme, "Reply to the invitation", content, "Send", cancel: "Cancel");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var response = answers.SelectedIndex switch
        {
            1 => AttendeeResponse.Tentative,
            2 => AttendeeResponse.Declined,
            _ => AttendeeResponse.Accepted,
        };
        return new Rsvp(response, note.Text.Trim());
    }

    public static async Task ShowErrorAsync(XamlRoot root, ElementTheme theme, string title, string text) =>
        await Dialog(root, theme, title, text, null, cancel: "OK").ShowAsync();

    private static ContentDialog Dialog(XamlRoot root, ElementTheme theme, string title, object? content, string? primary,
                                        string? secondary = null, string? cancel = null) => new()
    {
        Title = title,
        Content = content,
        PrimaryButtonText = primary ?? string.Empty,
        SecondaryButtonText = secondary ?? string.Empty,
        CloseButtonText = cancel ?? string.Empty,
        DefaultButton = primary is null ? ContentDialogButton.Close : ContentDialogButton.Primary,
        XamlRoot = root,
        RequestedTheme = theme,
        Style = EventVisuals.Resource<Style>("DefaultContentDialogStyle"),
    };
}

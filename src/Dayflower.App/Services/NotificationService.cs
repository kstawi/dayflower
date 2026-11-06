// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Dayflower.Services;

/// <summary>What the user did with a notification: clicked it ("open"), Join, Snooze (with a choice) or Dismiss.</summary>
public sealed record NotificationAction(string Action, string? Id, string? SnoozeChoice, string? Response = null);

/// <summary>The content of a new-invitation notification.</summary>
public sealed record InvitationToast(string Key, string Title, string When, string? From);

/// <summary>The content of a reminder notification.</summary>
public sealed record ReminderToast(string Id, string Title, string When, string? Detail, string? JoinLabel, bool KeepOnScreen, bool OfferSnooze);

/// <summary>Windows notifications through the Windows App SDK: reminders with Join, Snooze and Dismiss.</summary>
public sealed class NotificationService(ILogger<NotificationService> logger)
{
    private const string ReminderGroup = "reminders";
    private bool _registered;

    /// <summary>Raised (on a background thread) when the user acts on a notification while Dayflower is running.</summary>
    public event EventHandler<NotificationAction>? ActionInvoked;

    public void Register()
    {
        if (!AppNotificationManager.IsSupported())
        {
            logger.LogWarning("App notifications aren't supported here");
            return;
        }

        AppNotificationManager.Default.NotificationInvoked += (_, args) => ActionInvoked?.Invoke(this, Parse(args));
        AppNotificationManager.Default.Register();
        _registered = true;
    }

    public void Unregister()
    {
        if (_registered)
        {
            AppNotificationManager.Default.Unregister();
            _registered = false;
        }
    }

    public void ShowReminder(ReminderToast toast)
    {
        if (!_registered)
        {
            return;
        }

        var builder = new AppNotificationBuilder()
            .AddArgument("action", "open")
            .AddArgument("id", toast.Id)
            .AddText(toast.Title)
            .AddText(toast.When);
        if (toast.Detail is { Length: > 0 } detail)
        {
            builder.AddText(detail);
        }

        if (toast.KeepOnScreen)
        {
            builder.SetScenario(AppNotificationScenario.Reminder);
        }

        if (toast.JoinLabel is { } join)
        {
            builder.AddButton(new AppNotificationButton(join).AddArgument("action", "join").AddArgument("id", toast.Id));
        }

        if (toast.OfferSnooze)
        {
            builder.AddComboBox(new AppNotificationComboBox("snooze")
                    .AddItem("1", "1 minute")
                    .AddItem("5", "5 minutes")
                    .AddItem("10", "10 minutes")
                    .AddItem("15", "15 minutes")
                    .AddItem("start", "Until it starts")
                    .SetSelectedItem("5"))
                .AddButton(new AppNotificationButton("Snooze").AddArgument("action", "snooze").AddArgument("id", toast.Id));
        }

        builder.AddButton(new AppNotificationButton("Dismiss").AddArgument("action", "dismiss").AddArgument("id", toast.Id));

        var notification = builder.BuildNotification();

        // One notification per occurrence: a later reminder for the same event replaces the earlier one.
        notification.Tag = toast.Id;
        notification.Group = ReminderGroup;
        AppNotificationManager.Default.Show(notification);
        logger.LogInformation("Showed notification {Id}", toast.Id);
    }

    /// <summary>A new invitation, with Accept / Maybe / Decline; clicking it opens Dayflower on that day.</summary>
    public void ShowInvitation(InvitationToast toast)
    {
        if (!_registered)
        {
            return;
        }

        var builder = new AppNotificationBuilder()
            .AddArgument("action", "invite")
            .AddArgument("id", toast.Key)
            .AddText($"Invitation: {toast.Title}")
            .AddText(toast.When);
        if (toast.From is { Length: > 0 } from)
        {
            builder.AddText($"From {from}");
        }

        foreach (var (label, response) in new[] { ("Accept", "accepted"), ("Maybe", "tentative"), ("Decline", "declined") })
        {
            builder.AddButton(new AppNotificationButton(label)
                .AddArgument("action", "rsvp")
                .AddArgument("id", toast.Key)
                .AddArgument("response", response));
        }

        var notification = builder.BuildNotification();
        notification.Tag = toast.Key.Length > 64 ? toast.Key[^64..] : toast.Key;
        notification.Group = "invitations";
        AppNotificationManager.Default.Show(notification);
        logger.LogInformation("Showed invitation notification for {Key}", toast.Key);
    }

    /// <summary>A plain notice, such as "Dayflower is still running".</summary>
    public void ShowNotice(string title, string text)
    {
        if (_registered)
        {
            AppNotificationManager.Default.Show(new AppNotificationBuilder().AddArgument("action", "open").AddText(title).AddText(text).BuildNotification());
        }
    }

    public static NotificationAction Parse(AppNotificationActivatedEventArgs args) => new(
        args.Arguments.TryGetValue("action", out var action) ? action : "open",
        args.Arguments.TryGetValue("id", out var id) ? id : null,
        args.UserInput.TryGetValue("snooze", out var snooze) ? snooze : null,
        args.Arguments.TryGetValue("response", out var response) ? response : null);
}

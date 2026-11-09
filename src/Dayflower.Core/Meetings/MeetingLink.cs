// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

namespace Dayflower.Core.Meetings;

public enum MeetingProvider
{
    GoogleMeet,
    Zoom,
    MicrosoftTeams,
    Webex,
    SlackHuddle,
    Whereby,
    GoToMeeting,
    Jitsi,
    AmazonChime,

    /// <summary>A video link from the event's conference data whose service Dayflower doesn't know by name.</summary>
    Other,
}

/// <summary>The link to join an event's video meeting.</summary>
public sealed record MeetingLink(MeetingProvider Provider, Uri Uri)
{
    public string ProviderName => Provider switch
    {
        MeetingProvider.GoogleMeet => "Google Meet",
        MeetingProvider.Zoom => "Zoom",
        MeetingProvider.MicrosoftTeams => "Microsoft Teams",
        MeetingProvider.Webex => "Webex",
        MeetingProvider.SlackHuddle => "Slack huddle",
        MeetingProvider.Whereby => "Whereby",
        MeetingProvider.GoToMeeting => "GoTo Meeting",
        MeetingProvider.Jitsi => "Jitsi Meet",
        MeetingProvider.AmazonChime => "Amazon Chime",
        _ => "video call",
    };

    /// <summary>For buttons: "Join Google Meet", "Join Teams", "Join huddle".</summary>
    public string JoinLabel => "Join " + Provider switch
    {
        MeetingProvider.MicrosoftTeams => "Teams",
        MeetingProvider.SlackHuddle => "huddle",
        _ => ProviderName,
    };

    /// <summary>A short form of the link for display, e.g. "meet.google.com/abc-defg-hij" or "zoom.us".</summary>
    public string DisplayText => Provider is MeetingProvider.GoogleMeet or MeetingProvider.Jitsi or MeetingProvider.Whereby
        ? Uri.Host + Uri.AbsolutePath.TrimEnd('/')
        : Uri.Host;

    /// <summary>
    /// The link to open as a particular Google account. Google Meet's <c>authuser</c> parameter picks the account in a
    /// browser signed in to several, so a work meeting doesn't open as the personal account. Other services ignore it.
    /// </summary>
    public Uri ForAccount(string? email)
    {
        if (Provider != MeetingProvider.GoogleMeet || string.IsNullOrWhiteSpace(email))
        {
            return Uri;
        }

        var parameters = Uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("authuser=", StringComparison.OrdinalIgnoreCase))
            .Append("authuser=" + Uri.EscapeDataString(email.Trim()));
        return new UriBuilder(Uri) { Query = string.Join('&', parameters) }.Uri;
    }
}

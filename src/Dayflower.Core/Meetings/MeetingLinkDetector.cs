// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Net;
using System.Text.RegularExpressions;
using Dayflower.Core.Model;

namespace Dayflower.Core.Meetings;

/// <summary>
/// Finds the link to join an event's video meeting. Looks, in order, at the conference data the calendar attached
/// (Google Meet, or Zoom and Teams through their add-ons), Google's Meet link, the location, and the description.
/// Only links to known meeting services count, so documents, maps and other links in an invite are ignored.
/// </summary>
public static partial class MeetingLinkDetector
{
    public static MeetingLink? Find(CalendarEvent e)
    {
        var video = e.Conference?.EntryPoints.FirstOrDefault(p => p.Type == "video");
        if (video is not null && TryWebUri(video.Uri, out var conference))
        {
            conference = Unwrap(conference);
            return new MeetingLink(Classify(conference) ?? FromSolutionName(e.Conference!.SolutionName), conference);
        }

        return Recognize(e.HangoutLink) ?? FirstIn(e.Location) ?? FirstIn(e.Description);
    }

    /// <summary>The meeting link <paramref name="url"/> points to, or null if it isn't one.</summary>
    public static MeetingLink? Recognize(string? url)
    {
        if (!TryWebUri(url, out var uri))
        {
            return null;
        }

        uri = Unwrap(uri);
        return Classify(uri) is { } provider ? new MeetingLink(provider, uri) : null;
    }

    /// <summary>The links in a location or description, plain text or HTML, in the order they appear.</summary>
    internal static IEnumerable<string> ExtractUrls(string text) =>
        WebUrl().Matches(WebUtility.HtmlDecode(text)).Select(m => m.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '*', '>'));

    private static MeetingLink? FirstIn(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : ExtractUrls(text).Select(Recognize).FirstOrDefault(l => l is not null);

    /// <summary>Takes the real address out of redirect wrappers: Outlook Safe Links and Google's /url?q=.</summary>
    private static Uri Unwrap(Uri uri)
    {
        string host = uri.Host.ToLowerInvariant();
        string? inner = host.EndsWith(".safelinks.protection.outlook.com", StringComparison.Ordinal) ? QueryValue(uri, "url")
            : host is "www.google.com" or "google.com" && uri.AbsolutePath == "/url" ? QueryValue(uri, "q")
            : null;
        return TryWebUri(inner, out var unwrapped) ? unwrapped : uri;
    }

    private static MeetingProvider? Classify(Uri uri)
    {
        string host = uri.Host.ToLowerInvariant();
        string path = uri.AbsolutePath.ToLowerInvariant();
        bool hasPath = path.Length > 1;

        if (host == "meet.google.com")
        {
            return MeetCode().IsMatch(path) || path.StartsWith("/lookup/", StringComparison.Ordinal) ? MeetingProvider.GoogleMeet : null;
        }

        if (IsDomain(host, "zoom.us") || IsDomain(host, "zoomgov.com"))
        {
            return path.StartsWith("/j/", StringComparison.Ordinal) || path.StartsWith("/my/", StringComparison.Ordinal)
                   || path.StartsWith("/s/", StringComparison.Ordinal) || path.StartsWith("/w/", StringComparison.Ordinal)
                   || path.StartsWith("/wc/", StringComparison.Ordinal)
                ? MeetingProvider.Zoom
                : null;
        }

        if (host == "teams.microsoft.com")
        {
            return path.StartsWith("/l/meetup-join/", StringComparison.Ordinal) || path.StartsWith("/meet/", StringComparison.Ordinal)
                ? MeetingProvider.MicrosoftTeams
                : null;
        }

        if (host == "teams.live.com")
        {
            return path.StartsWith("/meet/", StringComparison.Ordinal) ? MeetingProvider.MicrosoftTeams : null;
        }

        if (IsDomain(host, "webex.com"))
        {
            return path.Contains("/j.php", StringComparison.Ordinal) || path.Contains("/meet/", StringComparison.Ordinal)
                   || path.Contains("/join/", StringComparison.Ordinal) || path.Contains("/wbxmjs/joinservice", StringComparison.Ordinal)
                ? MeetingProvider.Webex
                : null;
        }

        return host switch
        {
            "app.slack.com" when path.StartsWith("/huddle/", StringComparison.Ordinal) => MeetingProvider.SlackHuddle,
            _ when IsDomain(host, "whereby.com") && hasPath => MeetingProvider.Whereby,
            "meet.goto.com" when hasPath => MeetingProvider.GoToMeeting,
            "global.gotomeeting.com" when path.StartsWith("/join/", StringComparison.Ordinal) => MeetingProvider.GoToMeeting,
            "app.goto.com" when path.StartsWith("/meeting/", StringComparison.Ordinal) => MeetingProvider.GoToMeeting,
            "meet.jit.si" when hasPath => MeetingProvider.Jitsi,
            "chime.aws" when hasPath => MeetingProvider.AmazonChime,
            _ => null,
        };
    }

    private static MeetingProvider FromSolutionName(string? name) =>
        name is null ? MeetingProvider.Other
        : name.Contains("Zoom", StringComparison.OrdinalIgnoreCase) ? MeetingProvider.Zoom
        : name.Contains("Teams", StringComparison.OrdinalIgnoreCase) ? MeetingProvider.MicrosoftTeams
        : name.Contains("Webex", StringComparison.OrdinalIgnoreCase) ? MeetingProvider.Webex
        : name.Contains("Meet", StringComparison.OrdinalIgnoreCase) ? MeetingProvider.GoogleMeet
        : MeetingProvider.Other;

    private static bool IsDomain(string host, string domain) =>
        host == domain || host.EndsWith("." + domain, StringComparison.Ordinal);

    private static bool TryWebUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var parsed) && parsed.Scheme is "https" or "http")
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static string? QueryValue(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&'))
        {
            int eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && pair[..eq] == name)
            {
                return Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            }
        }

        return null;
    }

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex WebUrl();

    [GeneratedRegex(@"^/[a-z]{3}-[a-z]{4}-[a-z]{3}$")]
    private static partial Regex MeetCode();
}

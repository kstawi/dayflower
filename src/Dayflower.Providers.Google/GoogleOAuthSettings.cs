// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Reflection;

namespace Dayflower.Providers.Google;

/// <summary>
/// Dayflower's Google OAuth client. Google treats a desktop app's client secret as not confidential (it ships
/// inside the app); it's kept out of the public repo only to discourage reuse of the app's quota.
/// </summary>
public sealed record GoogleOAuthSettings(string ClientId, string ClientSecret)
{
    public const string CalendarListReadOnlyScope = "https://www.googleapis.com/auth/calendar.calendarlist.readonly";
    public const string EventsScope = "https://www.googleapis.com/auth/calendar.events";
    public const string EventsFreeBusyScope = "https://www.googleapis.com/auth/calendar.events.freebusy";

    /// <summary>
    /// Everything Dayflower asks for: who the account is, its calendar list, read/write access to events (for
    /// editing and RSVPs) and free/busy times (for guest availability). The narrowest set that covers the plan.
    /// </summary>
    public static IReadOnlyList<string> Scopes { get; } =
        ["openid", "email", "profile", CalendarListReadOnlyScope, EventsScope, EventsFreeBusyScope];

    /// <summary>Scopes Dayflower can't work without; free/busy is optional.</summary>
    public static IReadOnlyList<string> RequiredScopes { get; } = [CalendarListReadOnlyScope, EventsScope];

    /// <summary>The client this build was made with, or null if it was built without one.</summary>
    public static GoogleOAuthSettings? FromBuild()
    {
        var metadata = typeof(GoogleOAuthSettings).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .ToDictionary(a => a.Key, a => a.Value);
        string? id = metadata.GetValueOrDefault("GoogleOAuthClientId");
        string? secret = metadata.GetValueOrDefault("GoogleOAuthClientSecret");
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(secret) ? null : new GoogleOAuthSettings(id, secret);
    }
}

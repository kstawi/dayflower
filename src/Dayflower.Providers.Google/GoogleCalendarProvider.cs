// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Text.Json;
using Dayflower.Core.Model;
using Dayflower.Core.Providers;
using Dayflower.Providers.Google.Api;
using Dayflower.Providers.Google.Mapping;
using Dayflower.Providers.Google.OAuth;
using Microsoft.Extensions.Logging;

namespace Dayflower.Providers.Google;

/// <summary>Google Calendar.</summary>
/// <param name="settings">The OAuth client, or null for a build without one (it can't sign in).</param>
/// <param name="openBrowser">Opens a URL in the user's default browser.</param>
public sealed class GoogleCalendarProvider(
    GoogleOAuthSettings? settings,
    HttpClient http,
    Func<Uri, Task> openBrowser,
    TimeProvider time,
    ILoggerFactory loggers) : ICalendarProvider
{
    public const string ProviderId = "google";

    private readonly GoogleOAuthClient? _oauth = settings is null ? null : new GoogleOAuthClient(settings, http, time);

    public string Id => ProviderId;

    public bool IsConfigured => _oauth is not null;

    public async Task<NewAccount> SignInAsync(string? loginHint, CancellationToken cancellationToken)
    {
        var signIn = await OAuth.SignInAsync(openBrowser, loginHint, cancellationToken);
        return new NewAccount($"{ProviderId}:{signIn.Subject}", signIn.Email, signIn.Name, signIn.RefreshToken);
    }

    public ICalendarConnection Connect(string accountId, string secret)
    {
        var tokens = new GoogleTokenSource(OAuth, secret, time);
        var api = new GoogleCalendarApi(http, tokens, loggers.CreateLogger<GoogleCalendarApi>());
        return new GoogleCalendarConnection(accountId, api, OAuth, tokens);
    }

    public CalendarEvent ParseEvent(string accountId, string calendarId, string payload)
    {
        using var json = JsonDocument.Parse(payload);
        return GoogleEventMapper.Map(json.RootElement, accountId, calendarId);
    }

    private GoogleOAuthClient OAuth => _oauth
        ?? throw new InvalidOperationException("This build of Dayflower has no Google OAuth client.");
}

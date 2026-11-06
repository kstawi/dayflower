// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using System.Text;
using System.Text.Json;
using Dayflower.Core.Model;
using Dayflower.Core.Providers;
using Dayflower.Providers.Google.Api;
using Dayflower.Providers.Google.Mapping;
using Dayflower.Providers.Google.OAuth;

namespace Dayflower.Providers.Google;

/// <summary>Google Calendar API access for one signed-in account.</summary>
internal sealed class GoogleCalendarConnection(
    string accountId, GoogleCalendarApi api, GoogleOAuthClient oauth, GoogleTokenSource tokens) : ICalendarConnection
{
    public async Task<CalendarListChanges> GetCalendarChangesAsync(string? syncToken, CancellationToken cancellationToken)
    {
        var changed = new List<CalendarRecord>();
        var removed = new List<string>();
        string? pageToken = null, nextSyncToken = null;
        do
        {
            var url = new StringBuilder("users/me/calendarList?maxResults=250");
            AppendIfSet(url, "syncToken", syncToken);
            AppendIfSet(url, "pageToken", pageToken);

            using var page = await api.GetAsync(url.ToString(), cancellationToken);
            var root = page.RootElement;
            foreach (var entry in Items(root))
            {
                if (GoogleCalendarMapper.IsRemoved(entry))
                {
                    removed.Add(GoogleEventMapper.Str(entry, "id")!);
                }
                else
                {
                    changed.Add(GoogleCalendarMapper.Map(entry, accountId));
                }
            }

            pageToken = GoogleEventMapper.Str(root, "nextPageToken");
            nextSyncToken = GoogleEventMapper.Str(root, "nextSyncToken") ?? nextSyncToken;
        }
        while (pageToken is not null);

        return new CalendarListChanges(syncToken is null, changed, removed, nextSyncToken);
    }

    public async Task<EventChangeSet> GetEventChangesAsync(string calendarId, string? syncToken, CancellationToken cancellationToken)
    {
        var changes = new List<EventChange>();
        string? pageToken = null, nextSyncToken = null;
        do
        {
            // singleEvents=false: series come back once, with their changed and cancelled occurrences.
            var url = new StringBuilder("calendars/").Append(Uri.EscapeDataString(calendarId))
                .Append("/events?maxResults=2500&singleEvents=false");
            AppendIfSet(url, "syncToken", syncToken);
            AppendIfSet(url, "pageToken", pageToken);

            using var page = await api.GetAsync(url.ToString(), cancellationToken);
            var root = page.RootElement;
            foreach (var item in Items(root))
            {
                changes.Add(ToChange(item, calendarId));
            }

            pageToken = GoogleEventMapper.Str(root, "nextPageToken");
            nextSyncToken = GoogleEventMapper.Str(root, "nextSyncToken") ?? nextSyncToken;
        }
        while (pageToken is not null);

        return new EventChangeSet(syncToken is null, changes, nextSyncToken);
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetOccurrencesAsync(
        string calendarId, string seriesId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var result = new List<CalendarEvent>();
        string? pageToken = null;
        do
        {
            var url = new StringBuilder("calendars/").Append(Uri.EscapeDataString(calendarId))
                .Append("/events/").Append(Uri.EscapeDataString(seriesId)).Append("/instances?maxResults=2500");
            AppendIfSet(url, "timeMin", from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            AppendIfSet(url, "timeMax", to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            AppendIfSet(url, "pageToken", pageToken);

            using var page = await api.GetAsync(url.ToString(), cancellationToken);
            // Cancelled items may carry nothing but an ID, so they're skipped before mapping.
            result.AddRange(Items(page.RootElement)
                .Where(i => GoogleEventMapper.Str(i, "status") != "cancelled")
                .Select(i => GoogleEventMapper.Map(i, accountId, calendarId)));
            pageToken = GoogleEventMapper.Str(page.RootElement, "nextPageToken");
        }
        while (pageToken is not null);

        return result;
    }

    public Task RevokeAsync(CancellationToken cancellationToken) => oauth.RevokeAsync(tokens.RefreshToken, cancellationToken);

    /// <summary>
    /// A cancelled one-off event or series means it's gone; a cancelled occurrence of a series is kept, because
    /// it's what removes that occurrence from the series.
    /// </summary>
    private EventChange ToChange(JsonElement item, string calendarId)
    {
        string id = GoogleEventMapper.Str(item, "id") ?? throw new FormatException("Event without an ID.");
        bool cancelled = GoogleEventMapper.Str(item, "status") == "cancelled";
        bool isOccurrence = GoogleEventMapper.Str(item, "recurringEventId") is not null;
        if (cancelled && !isOccurrence)
        {
            return new EventChange(id, null, null);
        }

        return new EventChange(id, GoogleEventMapper.Map(item, accountId, calendarId), item.GetRawText());
    }

    private static IEnumerable<JsonElement> Items(JsonElement root) =>
        root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray()
            : [];

    private static void AppendIfSet(StringBuilder url, string name, string? value)
    {
        if (value is not null)
        {
            url.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
        }
    }
}

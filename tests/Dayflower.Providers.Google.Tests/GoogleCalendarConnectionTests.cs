// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Net;
using Dayflower.Core.Model;
using Dayflower.Core.Providers;
using Dayflower.Providers.Google.Api;
using Dayflower.Providers.Google.OAuth;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dayflower.Providers.Google.Tests;

public class GoogleCalendarConnectionTests
{
    private readonly FakeHttp _http = new FakeHttp().OnToken();

    private ICalendarConnection Connect()
    {
        var client = new HttpClient(_http);
        var oauth = new GoogleOAuthClient(new GoogleOAuthSettings("client-id", "client-secret"), client, TimeProvider.System);
        var tokens = new GoogleTokenSource(oauth, "refresh-1", TimeProvider.System);
        var api = new GoogleCalendarApi(client, tokens, NullLogger.Instance, (_, _) => Task.CompletedTask);
        return new GoogleCalendarConnection("google:1", api, oauth, tokens);
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public async Task Calendar_list_drops_hidden_and_deleted_calendars()
    {
        _http.On("calendarList", HttpStatusCode.OK, Fixture("calendar-list.json"));

        var changes = await Connect().GetCalendarChangesAsync(null, CancellationToken.None);

        Assert.True(changes.IsFullSync);
        Assert.Equal("list-sync-1", changes.NextSyncToken);
        Assert.Equal(["Me@example.com".ToLowerInvariant(), "Team", "Holidays in Poland"], changes.Changed.Select(c => c.Name));
        Assert.Equal(["hidden@group.calendar.google.com", "gone@group.calendar.google.com"], changes.RemovedIds);

        var primary = changes.Changed[0];
        Assert.True(primary.IsPrimary);
        Assert.True(primary.IsVisible);
        Assert.Equal(CalendarAccess.Owner, primary.Access);
        Assert.Equal("#039be5", primary.BackgroundColor);
        Assert.Equal([new Reminder(ReminderMethod.Popup, 10)], primary.DefaultReminders);
        Assert.False(changes.Changed[2].IsVisible);
        Assert.Equal(CalendarAccess.Writer, changes.Changed[1].Access);
    }

    [Fact]
    public async Task Requests_carry_the_access_token()
    {
        _http.On("calendarList", HttpStatusCode.OK, Fixture("calendar-list.json"));

        await Connect().GetCalendarChangesAsync(null, CancellationToken.None);

        var request = Assert.Single(_http.Requests, r => r.Url.Contains("calendarList", StringComparison.Ordinal));
        Assert.Equal("Bearer access-1", request.Authorization);
        var refresh = Assert.Single(_http.Requests, r => r.Url.Contains("oauth2.googleapis.com/token", StringComparison.Ordinal));
        Assert.Contains("grant_type=refresh_token", refresh.Body);
        Assert.Contains("refresh_token=refresh-1", refresh.Body);
    }

    [Fact]
    public async Task Event_pages_are_combined_and_the_last_page_gives_the_sync_token()
    {
        _http.On("pageToken=p2", HttpStatusCode.OK, Fixture("events.json"))
             .On("/events?", HttpStatusCode.OK, """{"items":[{"id":"first","status":"confirmed","start":{"date":"2026-11-01"},"end":{"date":"2026-11-02"}}],"nextPageToken":"p2"}""");

        var changes = await Connect().GetEventChangesAsync("pl.polish#holiday@group.v.calendar.google.com", null, CancellationToken.None);

        Assert.True(changes.IsFullSync);
        Assert.Equal("sync-abc", changes.NextSyncToken);
        Assert.Equal(8, changes.Changes.Count);
        Assert.Contains(_http.Requests, r => r.Url.Contains("calendars/pl.polish%23holiday%40group.v.calendar.google.com/events", StringComparison.Ordinal));
        Assert.All(_http.UrlsContaining("/events"), u => Assert.Contains("singleEvents=false", u));
    }

    [Fact]
    public async Task Cancelled_one_off_event_is_a_removal_but_a_cancelled_occurrence_is_kept()
    {
        _http.On("/events?", HttpStatusCode.OK, Fixture("events.json"));

        var changes = await Connect().GetEventChangesAsync("me@example.com", "old-token", CancellationToken.None);

        Assert.False(changes.IsFullSync);
        Assert.True(Assert.Single(changes.Changes, c => c.EventId == "deleted").IsRemoved);
        var cancelledOccurrence = Assert.Single(changes.Changes, c => c.EventId == "series_20261118T080000Z");
        Assert.Equal(EventStatus.Cancelled, cancelledOccurrence.Event!.Status);
        Assert.NotNull(cancelledOccurrence.Payload);
        Assert.Contains(_http.Requests, r => r.Url.Contains("syncToken=old-token", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Gone_means_the_sync_token_expired()
    {
        _http.On("/events?", HttpStatusCode.Gone, """{"error":{"code":410,"message":"Sync token is no longer valid, a full sync is required.","errors":[{"reason":"fullSyncRequired"}]}}""");

        await Assert.ThrowsAsync<SyncTokenExpiredException>(
            () => Connect().GetEventChangesAsync("me@example.com", "old-token", CancellationToken.None));
    }

    [Fact]
    public async Task Unauthorized_refreshes_the_token_once_and_retries()
    {
        _http.On("oauth2.googleapis.com/token", HttpStatusCode.OK, """{"access_token":"access-2","expires_in":3599}""")
             .On("calendarList", HttpStatusCode.Unauthorized, """{"error":{"code":401,"message":"Invalid Credentials"}}""")
             .On("calendarList", HttpStatusCode.OK, Fixture("calendar-list.json"));

        var changes = await Connect().GetCalendarChangesAsync(null, CancellationToken.None);

        Assert.Equal(3, changes.Changed.Count);
        Assert.Equal(["Bearer access-1", "Bearer access-2"],
                     _http.Requests.Where(r => r.Url.Contains("calendarList", StringComparison.Ordinal)).Select(r => r.Authorization));
    }

    [Fact]
    public async Task Rate_limit_is_retried()
    {
        _http.On("calendarList", HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"Rate Limit Exceeded","errors":[{"reason":"rateLimitExceeded"}]}}""")
             .On("calendarList", HttpStatusCode.OK, Fixture("calendar-list.json"));

        var changes = await Connect().GetCalendarChangesAsync(null, CancellationToken.None);

        Assert.Equal(3, changes.Changed.Count);
        Assert.Equal(2, _http.UrlsContaining("calendarList").Count());
    }

    [Fact]
    public async Task Other_errors_are_not_retried()
    {
        _http.On("/events?", HttpStatusCode.NotFound, """{"error":{"code":404,"message":"Not Found","errors":[{"reason":"notFound"}]}}""");

        var ex = await Assert.ThrowsAsync<GoogleApiException>(
            () => Connect().GetEventChangesAsync("gone@example.com", null, CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, ex.Status);
        Assert.Single(_http.UrlsContaining("/events"));
    }

    [Fact]
    public async Task Revoked_refresh_token_means_sign_in_again()
    {
        var http = new FakeHttp().On("oauth2.googleapis.com/token", HttpStatusCode.BadRequest,
                                     """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""");
        var client = new HttpClient(http);
        var oauth = new GoogleOAuthClient(new GoogleOAuthSettings("client-id", "client-secret"), client, TimeProvider.System);
        var tokens = new GoogleTokenSource(oauth, "refresh-1", TimeProvider.System);
        var connection = new GoogleCalendarConnection(
            "google:1", new GoogleCalendarApi(client, tokens, NullLogger.Instance), oauth, tokens);

        await Assert.ThrowsAsync<AccountSignInRequiredException>(() => connection.GetCalendarChangesAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task Occurrences_come_from_the_instances_endpoint_without_cancelled_ones()
    {
        _http.On("/instances?", HttpStatusCode.OK, Fixture("events.json"));

        var occurrences = await Connect().GetOccurrencesAsync(
            "me@example.com", "series", DateTimeOffset.Parse("2026-11-01T00:00:00Z"), DateTimeOffset.Parse("2026-12-01T00:00:00Z"),
            CancellationToken.None);

        Assert.DoesNotContain(occurrences, o => o.Status == EventStatus.Cancelled);
        var url = Assert.Single(_http.UrlsContaining("/instances"));
        Assert.Contains("events/series/instances", url);
        Assert.Contains("timeMin=2026-11-01T00%3A00%3A00Z", url);
    }
}

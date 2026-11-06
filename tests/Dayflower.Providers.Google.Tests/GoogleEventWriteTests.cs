// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dayflower.Core.Editing;
using Dayflower.Core.Model;
using Dayflower.Core.Providers;
using Dayflower.Providers.Google.Api;
using Dayflower.Providers.Google.Mapping;
using Dayflower.Providers.Google.OAuth;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dayflower.Providers.Google.Tests;

public class GoogleEventWriteTests
{
    private const string Calendar = "me@example.com";
    private const string Warsaw = "Europe/Warsaw";

    /// <summary>A weekly half-hour meeting on Thursdays at 9:00 Warsaw time, ten times from 5 November 2026.</summary>
    private const string SeriesJson = """
        {"kind":"calendar#event","etag":"\"1\"","id":"series","status":"confirmed","htmlLink":"https://www.google.com/calendar/event?eid=x",
         "summary":"Weekly sync","iCalUID":"series@google.com","sequence":0,
         "creator":{"email":"me@example.com","self":true},"organizer":{"email":"me@example.com","self":true},
         "start":{"dateTime":"2026-11-05T09:00:00+01:00","timeZone":"Europe/Warsaw"},
         "end":{"dateTime":"2026-11-05T09:30:00+01:00","timeZone":"Europe/Warsaw"},
         "recurrence":["RRULE:FREQ=WEEKLY;COUNT=10"],
         "attendees":[{"email":"me@example.com","self":true,"organizer":true,"responseStatus":"accepted"},
                      {"email":"anna@example.com","responseStatus":"accepted"}],
         "reminders":{"useDefault":true}}
        """;

    private const string OneOffJson = """
        {"id":"lunch","status":"confirmed","summary":"Lunch","organizer":{"email":"me@example.com","self":true},
         "start":{"dateTime":"2026-11-10T12:00:00+01:00","timeZone":"Europe/Warsaw"},
         "end":{"dateTime":"2026-11-10T13:00:00+01:00","timeZone":"Europe/Warsaw"},
         "attendees":[{"email":"me@example.com","self":true,"organizer":true,"responseStatus":"accepted"},
                      {"email":"anna@example.com","responseStatus":"accepted","comment":"Yum"},
                      {"email":"bob@example.com","responseStatus":"declined"},
                      {"email":"room@resource.calendar.google.com","resource":true,"responseStatus":"accepted"}]}
        """;

    private readonly FakeHttp _http = new FakeHttp().OnToken();

    private ICalendarConnection Connect()
    {
        var client = new HttpClient(_http);
        var oauth = new GoogleOAuthClient(new GoogleOAuthSettings("client-id", "client-secret"), client, TimeProvider.System);
        var tokens = new GoogleTokenSource(oauth, "refresh-1", TimeProvider.System);
        var api = new GoogleCalendarApi(client, tokens, NullLogger.Instance, (_, _) => Task.CompletedTask);
        return new GoogleCalendarConnection("google:1", api, oauth, tokens);
    }

    private IEnumerable<(string Url, string? Authorization, string? Body, string Method)> ApiRequests =>
        _http.Requests.Where(r => r.Url.Contains("/calendar/v3/", StringComparison.Ordinal));

    private static CalendarEvent Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return GoogleEventMapper.Map(doc.RootElement, "google:1", Calendar);
    }

    private static EventTime At(string iso, string? zone = Warsaw) => EventTime.At(DateTimeOffset.Parse(iso), zone);

    /// <summary>The series' third occurrence, on 19 November.</summary>
    private static EventTarget ThirdOccurrence() =>
        new(Parse(SeriesJson), At("2026-11-19T09:00:00+01:00"), At("2026-11-19T09:00:00+01:00"), At("2026-11-19T09:30:00+01:00"));

    private static JsonNode Body((string Url, string? Authorization, string? Body, string Method) request) => JsonNode.Parse(request.Body!)!;

    private static string Echo(string id, string summary = "Saved") =>
        $$$"""{"id":"{{{id}}}","status":"confirmed","summary":"{{{summary}}}","start":{"dateTime":"2026-11-19T09:00:00+01:00"},"end":{"dateTime":"2026-11-19T09:30:00+01:00"}}""";

    [Fact]
    public async Task Create_posts_the_event_and_asks_for_a_meet_link()
    {
        _http.On("/events?sendUpdates", HttpStatusCode.OK, Echo("new-id", "Planning"));
        var edit = new EventEdit
        {
            Summary = "Planning",
            Start = At("2026-11-20T15:00:00+01:00"),
            End = At("2026-11-20T16:00:00+01:00"),
            Guests = [new GuestEdit("anna@example.com"), new GuestEdit("bob@example.com", IsOptional: true)],
            Meet = true,
        };

        var changes = await Connect().CreateEventAsync(Calendar, edit, notifyGuests: true, CancellationToken.None);

        var request = Assert.Single(ApiRequests);
        Assert.Equal("POST", request.Method);
        Assert.EndsWith("calendars/me%40example.com/events?sendUpdates=all&conferenceDataVersion=1", request.Url);
        var body = Body(request);
        Assert.Equal("Planning", (string?)body["summary"]);
        Assert.Equal("2026-11-20T15:00:00+01:00", (string?)body["start"]!["dateTime"]);
        Assert.Equal(Warsaw, (string?)body["start"]!["timeZone"]);
        Assert.Equal("hangoutsMeet", (string?)body["conferenceData"]!["createRequest"]!["conferenceSolutionKey"]!["type"]);
        Assert.Equal(["anna@example.com", "bob@example.com"], body["attendees"]!.AsArray().Select(a => (string?)a!["email"]));
        Assert.True((bool?)body["attendees"]![1]!["optional"]);
        Assert.Equal("Planning", Assert.Single(changes).Event!.Summary);
    }

    [Fact]
    public async Task Patch_sends_only_what_changed()
    {
        _http.On("events/lunch?", HttpStatusCode.OK, Echo("lunch", "Late lunch"));
        var target = new EventTarget(Parse(OneOffJson), null, At("2026-11-10T12:00:00+01:00"), At("2026-11-10T13:00:00+01:00"));

        var changes = await Connect().UpdateEventAsync(target, new EventEdit { Summary = "Late lunch" }, EditScope.ThisEvent,
                                                       notifyGuests: false, CancellationToken.None);

        var request = Assert.Single(ApiRequests);
        Assert.Equal("PATCH", request.Method);
        Assert.Contains("events/lunch?sendUpdates=none", request.Url);
        Assert.Equal("""{"summary":"Late lunch"}""", request.Body);
        Assert.Equal("lunch", Assert.Single(changes).EventId);
    }

    [Fact]
    public async Task Changing_guests_keeps_answers_the_user_and_rooms()
    {
        _http.On("events/lunch?", HttpStatusCode.OK, Echo("lunch"))
             .On("events/lunch", HttpStatusCode.OK, OneOffJson);
        var target = new EventTarget(Parse(OneOffJson), null, At("2026-11-10T12:00:00+01:00"), At("2026-11-10T13:00:00+01:00"));
        var edit = new EventEdit { Guests = [new GuestEdit("anna@example.com"), new GuestEdit("carol@example.com", IsOptional: true)] };

        await Connect().UpdateEventAsync(target, edit, EditScope.ThisEvent, notifyGuests: true, CancellationToken.None);

        var patch = Assert.Single(ApiRequests, r => r.Method == "PATCH");
        var attendees = Body(patch)["attendees"]!.AsArray();
        Assert.Equal(["me@example.com", "room@resource.calendar.google.com", "anna@example.com", "carol@example.com"],
                     attendees.Select(a => (string?)a!["email"]));
        Assert.Equal("accepted", (string?)attendees[2]!["responseStatus"]);
        Assert.Equal("Yum", (string?)attendees[2]!["comment"]);
        Assert.True((bool?)attendees[3]!["optional"]);
    }

    [Theory]
    [InlineData("series", "2026-11-19T09:00:00+01:00", "series_20261119T080000Z")]
    [InlineData("series_R20261105T080000", "2026-11-19T09:00:00+01:00", "series_20261119T080000Z")]
    public void Occurrence_is_addressed_by_its_instance_id(string seriesId, string originalStart, string expected)
    {
        var series = Parse(SeriesJson.Replace("\"id\":\"series\"", $"\"id\":\"{seriesId}\"", StringComparison.Ordinal));
        var start = At(originalStart);

        Assert.Equal(expected, GoogleCalendarConnection.TargetId(new EventTarget(series, start, start, start)));
    }

    [Fact]
    public void All_day_occurrence_is_addressed_by_its_date()
    {
        var series = Parse("""{"id":"birthday","start":{"date":"2026-03-14"},"end":{"date":"2026-03-15"},"recurrence":["RRULE:FREQ=YEARLY"]}""");
        var start = EventTime.AllDay(new DateOnly(2027, 3, 14));

        Assert.Equal("birthday_20270314", GoogleCalendarConnection.TargetId(new EventTarget(series, start, start, start)));
    }

    [Fact]
    public async Task This_event_patches_the_occurrence()
    {
        _http.On("events/series_20261119T080000Z?", HttpStatusCode.OK, Echo("series_20261119T080000Z"));

        await Connect().UpdateEventAsync(ThirdOccurrence(), new EventEdit { Summary = "Just this one" }, EditScope.ThisEvent,
                                         notifyGuests: true, CancellationToken.None);

        var request = Assert.Single(ApiRequests);
        Assert.Equal("PATCH", request.Method);
        Assert.Contains("events/series_20261119T080000Z?sendUpdates=all", request.Url);
    }

    [Fact]
    public async Task Moving_all_events_shifts_the_series_by_as_much()
    {
        _http.On("events/series?", HttpStatusCode.OK, Echo("series"))
             .On("events/series", HttpStatusCode.OK, SeriesJson);
        var edit = new EventEdit { Start = At("2026-11-20T10:00:00+01:00"), End = At("2026-11-20T11:00:00+01:00") };

        await Connect().UpdateEventAsync(ThirdOccurrence(), edit, EditScope.AllEvents, notifyGuests: true, CancellationToken.None);

        var patch = Assert.Single(ApiRequests, r => r.Method == "PATCH");
        Assert.Contains("events/series?", patch.Url);
        var body = Body(patch);
        Assert.Equal("2026-11-06T10:00:00+01:00", (string?)body["start"]!["dateTime"]);
        Assert.Equal("2026-11-06T11:00:00+01:00", (string?)body["end"]!["dateTime"]);
        Assert.Equal(Warsaw, (string?)body["start"]!["timeZone"]);
    }

    [Fact]
    public async Task This_and_following_ends_the_series_and_starts_a_new_one()
    {
        _http.On("events/series?", HttpStatusCode.OK, Echo("series"))
             .On("events/series", HttpStatusCode.OK, SeriesJson)
             .On("/events?sendUpdates", HttpStatusCode.OK, Echo("continuation", "Renamed"));

        var changes = await Connect().UpdateEventAsync(ThirdOccurrence(), new EventEdit { Summary = "Renamed" }, EditScope.ThisAndFollowing,
                                                       notifyGuests: true, CancellationToken.None);

        var truncate = Assert.Single(ApiRequests, r => r.Method == "PATCH");
        Assert.Equal("""{"recurrence":["RRULE:FREQ=WEEKLY;UNTIL=20261119T075959Z"]}""", truncate.Body);

        var create = Assert.Single(ApiRequests, r => r.Method == "POST");
        var body = Body(create);
        Assert.Equal("Renamed", (string?)body["summary"]);
        Assert.Equal("2026-11-19T09:00:00+01:00", (string?)body["start"]!["dateTime"]);
        Assert.Equal("2026-11-19T09:30:00+01:00", (string?)body["end"]!["dateTime"]);
        Assert.Equal(Warsaw, (string?)body["start"]!["timeZone"]);
        Assert.Equal("RRULE:FREQ=WEEKLY;COUNT=8", (string?)body["recurrence"]![0]);
        Assert.Equal(2, body["attendees"]!.AsArray().Count);
        foreach (var field in new[] { "id", "etag", "iCalUID", "htmlLink", "organizer", "creator", "sequence" })
        {
            Assert.Null(body[field]);
        }

        Assert.Equal(["series", "continuation"], changes.Select(c => c.EventId));
    }

    [Fact]
    public async Task This_and_following_from_a_moved_occurrence_continues_from_its_slot()
    {
        _http.On("events/series?", HttpStatusCode.OK, Echo("series"))
             .On("events/series", HttpStatusCode.OK, SeriesJson)
             .On("/events?sendUpdates", HttpStatusCode.OK, Echo("continuation"));
        var moved = Parse("""
            {"id":"series_20261119T080000Z","status":"confirmed","recurringEventId":"series",
             "originalStartTime":{"dateTime":"2026-11-19T09:00:00+01:00","timeZone":"Europe/Warsaw"},
             "start":{"dateTime":"2026-11-19T14:00:00+01:00","timeZone":"Europe/Warsaw"},
             "end":{"dateTime":"2026-11-19T14:30:00+01:00","timeZone":"Europe/Warsaw"}}
            """);
        var target = new EventTarget(moved, moved.OriginalStart, moved.Start, moved.End);

        await Connect().UpdateEventAsync(target, new EventEdit { Location = "Room 2" }, EditScope.ThisAndFollowing,
                                         notifyGuests: false, CancellationToken.None);

        var body = Body(Assert.Single(ApiRequests, r => r.Method == "POST"));
        Assert.Equal("2026-11-19T09:00:00+01:00", (string?)body["start"]!["dateTime"]);
        Assert.Equal("Room 2", (string?)body["location"]);
    }

    [Fact]
    public async Task Deleting_an_occurrence_leaves_a_cancelled_exception()
    {
        _http.On("events/series_20261119T080000Z?", HttpStatusCode.NoContent, string.Empty);

        var changes = await Connect().DeleteEventAsync(ThirdOccurrence(), EditScope.ThisEvent, notifyGuests: true, CancellationToken.None);

        var request = Assert.Single(ApiRequests);
        Assert.Equal("DELETE", request.Method);
        Assert.EndsWith("events/series_20261119T080000Z?sendUpdates=all", request.Url);
        var change = Assert.Single(changes);
        Assert.False(change.IsRemoved);
        Assert.Equal(EventStatus.Cancelled, change.Event!.Status);
        Assert.Equal("series", change.Event.RecurringEventId);
        Assert.Equal(At("2026-11-19T09:00:00+01:00").OccurrenceKey, change.Event.OriginalStart!.OccurrenceKey);
    }

    [Fact]
    public async Task Deleting_all_events_deletes_the_series()
    {
        _http.On("events/series?", HttpStatusCode.NoContent, string.Empty)
             .On("events/series", HttpStatusCode.OK, SeriesJson);

        var changes = await Connect().DeleteEventAsync(ThirdOccurrence(), EditScope.AllEvents, notifyGuests: false, CancellationToken.None);

        Assert.Single(ApiRequests, r => r.Method == "DELETE" && r.Url.EndsWith("events/series?sendUpdates=none", StringComparison.Ordinal));
        Assert.True(Assert.Single(changes).IsRemoved);
    }

    [Fact]
    public async Task Deleting_this_and_following_ends_the_series_early()
    {
        _http.On("events/series?", HttpStatusCode.OK, Echo("series"))
             .On("events/series", HttpStatusCode.OK, SeriesJson);

        await Connect().DeleteEventAsync(ThirdOccurrence(), EditScope.ThisAndFollowing, notifyGuests: true, CancellationToken.None);

        var patch = Assert.Single(ApiRequests, r => r.Method == "PATCH");
        Assert.Equal("""{"recurrence":["RRULE:FREQ=WEEKLY;UNTIL=20261119T075959Z"]}""", patch.Body);
        Assert.DoesNotContain(ApiRequests, r => r.Method == "DELETE");
    }

    [Fact]
    public async Task Answering_changes_only_the_users_own_answer()
    {
        const string invitation = """
            {"id":"invite","status":"confirmed","organizer":{"email":"boss@example.com"},
             "start":{"dateTime":"2026-11-10T12:00:00+01:00"},"end":{"dateTime":"2026-11-10T13:00:00+01:00"},
             "attendees":[{"email":"boss@example.com","organizer":true,"responseStatus":"accepted"},
                          {"email":"me@example.com","self":true,"responseStatus":"needsAction"},
                          {"email":"anna@example.com","responseStatus":"tentative","comment":"Maybe"}]}
            """;
        _http.On("events/invite?", HttpStatusCode.OK, Echo("invite"))
             .On("events/invite", HttpStatusCode.OK, invitation);
        var target = new EventTarget(Parse(invitation), null, At("2026-11-10T12:00:00+01:00", null), At("2026-11-10T13:00:00+01:00", null));

        await Connect().RespondAsync(target, new Rsvp(AttendeeResponse.Accepted, "See you there"), EditScope.ThisEvent, CancellationToken.None);

        var patch = Assert.Single(ApiRequests, r => r.Method == "PATCH");
        Assert.EndsWith("events/invite?sendUpdates=all", patch.Url);
        var attendees = Body(patch)["attendees"]!.AsArray();
        Assert.Equal(3, attendees.Count);
        Assert.Equal("accepted", (string?)attendees[1]!["responseStatus"]);
        Assert.Equal("See you there", (string?)attendees[1]!["comment"]);
        Assert.Equal("tentative", (string?)attendees[2]!["responseStatus"]);
        Assert.Equal("Maybe", (string?)attendees[2]!["comment"]);
    }

    [Fact]
    public async Task Answering_all_events_answers_the_series()
    {
        _http.On("events/series?", HttpStatusCode.OK, Echo("series"))
             .On("events/series", HttpStatusCode.OK, SeriesJson);

        await Connect().RespondAsync(ThirdOccurrence(), new Rsvp(AttendeeResponse.Declined), EditScope.AllEvents, CancellationToken.None);

        Assert.Single(ApiRequests, r => r.Method == "PATCH" && r.Url.Contains("events/series?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Free_busy_reads_busy_times_and_hidden_calendars()
    {
        _http.On("freeBusy", HttpStatusCode.OK, """
            {"kind":"calendar#freeBusy","calendars":{
              "anna@example.com":{"busy":[{"start":"2026-11-10T08:00:00Z","end":"2026-11-10T09:00:00Z"}]},
              "stranger@other.com":{"errors":[{"domain":"global","reason":"notFound"}],"busy":[]}}}
            """);

        var result = await Connect().GetFreeBusyAsync(
            ["anna@example.com", "stranger@other.com"], DateTimeOffset.Parse("2026-11-10T00:00:00Z"), DateTimeOffset.Parse("2026-11-11T00:00:00Z"),
            CancellationToken.None);

        var request = Assert.Single(ApiRequests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("2026-11-10T00:00:00Z", (string?)Body(request)["timeMin"]);
        Assert.Equal(2, Body(request)["items"]!.AsArray().Count);
        var anna = result["ANNA@example.com"];
        Assert.False(anna.IsUnknown);
        Assert.Equal(DateTimeOffset.Parse("2026-11-10T08:00:00Z"), Assert.Single(anna.Busy).Start);
        Assert.True(result["stranger@other.com"].IsUnknown);
    }

    [Fact]
    public async Task A_failed_create_is_not_repeated_but_a_failed_patch_is()
    {
        const string unavailable = """{"error":{"code":503,"message":"Backend Error","errors":[{"reason":"backendError"}]}}""";
        _http.On("/events?sendUpdates", HttpStatusCode.ServiceUnavailable, unavailable)
             .On("events/lunch?", HttpStatusCode.ServiceUnavailable, unavailable)
             .On("events/lunch?", HttpStatusCode.OK, Echo("lunch"));
        var connection = Connect();
        var edit = new EventEdit { Summary = "x", Start = At("2026-11-10T12:00:00+01:00"), End = At("2026-11-10T13:00:00+01:00") };

        await Assert.ThrowsAsync<GoogleApiException>(() => connection.CreateEventAsync(Calendar, edit, false, CancellationToken.None));
        Assert.Single(ApiRequests, r => r.Method == "POST");

        var target = new EventTarget(Parse(OneOffJson), null, edit.Start!, edit.End!);
        await connection.UpdateEventAsync(target, new EventEdit { Summary = "y" }, EditScope.ThisEvent, false, CancellationToken.None);
        Assert.Equal(2, ApiRequests.Count(r => r.Method == "PATCH"));
    }

    [Fact]
    public async Task Gone_outside_a_sync_is_an_ordinary_error()
    {
        _http.On("events/series", HttpStatusCode.Gone, """{"error":{"code":410,"message":"Resource has been deleted"}}""");

        var ex = await Assert.ThrowsAsync<GoogleApiException>(
            () => Connect().UpdateEventAsync(ThirdOccurrence(), new EventEdit { Summary = "x" }, EditScope.AllEvents, false, CancellationToken.None));
        Assert.Equal(HttpStatusCode.Gone, ex.Status);
    }

    [Fact]
    public void Writer_removes_the_meeting_and_colour_with_nulls()
    {
        var json = GoogleEventWriter.Build(new EventEdit { Meet = false, ColorId = string.Empty }).ToJsonString();

        Assert.Equal("""{"conferenceData":null,"colorId":null}""", json);
    }

    [Fact]
    public void Writer_writes_reminders_and_all_day_times()
    {
        var body = GoogleEventWriter.Build(new EventEdit
        {
            Start = EventTime.AllDay(new DateOnly(2026, 12, 24)),
            End = EventTime.AllDay(new DateOnly(2026, 12, 27)),
            Reminders = new ReminderSettings(false, [new Reminder(ReminderMethod.Popup, 30), new Reminder(ReminderMethod.Email, 1440)]),
            IsBusy = false,
        });

        Assert.Equal("2026-12-24", (string?)body["start"]!["date"]);
        Assert.Null(body["start"]!["dateTime"]);
        Assert.True(body["start"]!.AsObject().ContainsKey("dateTime"));
        Assert.Equal("""{"useDefault":false,"overrides":[{"method":"popup","minutes":30},{"method":"email","minutes":1440}]}""",
                     body["reminders"]!.ToJsonString());
        Assert.Equal("transparent", (string?)body["transparency"]);
    }

    [Fact]
    public void Writer_writes_workspace_event_types()
    {
        var ooo = GoogleEventWriter.Build(new EventEdit
        {
            Kind = EventKind.OutOfOffice,
            Workspace = new WorkspaceDetails(AutoDeclineMode.AllConflicting, "On holiday"),
        });
        var office = GoogleEventWriter.Build(new EventEdit
        {
            Kind = EventKind.WorkingLocation,
            Workspace = new WorkspaceDetails(Location: WorkingLocationType.Office, LocationLabel: "Kraków"),
        });

        Assert.Equal("outOfOffice", (string?)ooo["eventType"]);
        Assert.Equal("declineAllConflictingInvitations", (string?)ooo["outOfOfficeProperties"]!["autoDeclineMode"]);
        Assert.Equal("On holiday", (string?)ooo["outOfOfficeProperties"]!["declineMessage"]);
        Assert.Equal("officeLocation", (string?)office["workingLocationProperties"]!["type"]);
        Assert.Equal("Kraków", (string?)office["workingLocationProperties"]!["officeLocation"]!["label"]);
    }

    [Fact]
    public void Mapper_reads_guest_permissions_and_workspace_details()
    {
        var e = Parse("""
            {"id":"wl","start":{"date":"2026-11-10"},"end":{"date":"2026-11-11"},"eventType":"workingLocation",
             "guestsCanModify":true,"guestsCanInviteOthers":false,
             "workingLocationProperties":{"type":"customLocation","customLocation":{"label":"Café"}}}
            """);

        Assert.True(e.GuestsCanModify);
        Assert.False(e.GuestsCanInviteOthers);
        Assert.True(e.GuestsCanSeeOtherGuests);
        Assert.Equal(new WorkspaceDetails(Location: WorkingLocationType.Custom, LocationLabel: "Café"), e.Workspace);
    }
}

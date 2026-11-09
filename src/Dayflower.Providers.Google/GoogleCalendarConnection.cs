// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dayflower.Core.Editing;
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

    public async Task<IReadOnlyList<CalendarEvent>> GetExpandedEventsAsync(
        string calendarId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var result = new List<CalendarEvent>();
        string? pageToken = null;
        do
        {
            var url = new StringBuilder("calendars/").Append(Uri.EscapeDataString(calendarId))
                .Append("/events?maxResults=2500&singleEvents=true");
            AppendIfSet(url, "timeMin", from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            AppendIfSet(url, "timeMax", to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            AppendIfSet(url, "pageToken", pageToken);

            using var page = await api.GetAsync(url.ToString(), cancellationToken);
            result.AddRange(Items(page.RootElement)
                .Where(i => GoogleEventMapper.Str(i, "status") != "cancelled")
                .Select(i => GoogleEventMapper.Map(i, accountId, calendarId)));
            pageToken = GoogleEventMapper.Str(page.RootElement, "nextPageToken");
        }
        while (pageToken is not null);

        return result;
    }

    public Task RevokeAsync(CancellationToken cancellationToken) => oauth.RevokeAsync(tokens.RefreshToken, cancellationToken);

    public async Task<IReadOnlyList<EventChange>> CreateEventAsync(string calendarId, EventEdit edit, bool notifyGuests, CancellationToken cancellationToken)
    {
        using var created = await api.SendAsync(HttpMethod.Post, EventsUrl(calendarId) + Query(notifyGuests),
                                                GoogleEventWriter.Build(edit), cancellationToken);
        return [ToChange(created!.RootElement, calendarId)];
    }

    public async Task<IReadOnlyList<EventChange>> UpdateEventAsync(
        EventTarget target, EventEdit edit, EditScope scope, bool notifyGuests, CancellationToken cancellationToken)
    {
        string calendarId = target.CalendarId;
        if (!target.IsRepeating || scope == EditScope.ThisEvent)
        {
            string id = TargetId(target);
            using var current = edit.Guests is not null || edit.Meet == true ? await GetEventAsync(calendarId, id, cancellationToken) : null;
            using var updated = await api.SendAsync(HttpMethod.Patch, EventUrl(calendarId, id) + Query(notifyGuests),
                                                    GoogleEventWriter.Build(edit, current?.RootElement), cancellationToken);
            return [ToChange(updated!.RootElement, calendarId)];
        }

        using var seriesJson = await GetEventAsync(calendarId, target.SeriesId!, cancellationToken);
        var series = GoogleEventMapper.Map(seriesJson.RootElement, accountId, calendarId);
        var occurrenceStart = target.OriginalStart ?? target.OccurrenceStart;
        bool isFirst = series.Start.OccurrenceKey == occurrenceStart.OccurrenceKey;

        if (scope == EditScope.AllEvents || isFirst)
        {
            // Moving one occurrence with "all events" moves the whole series by as much.
            if (edit.Start is not null && edit.End is not null)
            {
                var (start, end) = SeriesShift.Move(series, occurrenceStart, edit.Start, edit.End);
                edit = edit with { Start = start, End = end };
            }

            using var updated = await api.SendAsync(HttpMethod.Patch, EventUrl(calendarId, series.Key.EventId) + Query(notifyGuests),
                                                    GoogleEventWriter.Build(edit, seriesJson.RootElement), cancellationToken);
            return [ToChange(updated!.RootElement, calendarId)];
        }

        // This and following: the series ends before this occurrence and a new series continues from it. Without a
        // new time it continues from the occurrence's slot in the pattern (not from where a moved occurrence went),
        // and a repeating event needs a time zone, so it keeps the series' one.
        var (newStart, newEnd) = edit.Start is not null && edit.End is not null
            ? (WithZone(edit.Start, series.Start.TimeZone), WithZone(edit.End, series.Start.TimeZone))
            : Slot(series, occurrenceStart);
        var recurrence = edit.Recurrence ?? SeriesSplit.ContinueAfter(series.Recurrence, SeriesSplit.OccurrencesBefore(series, occurrenceStart));
        var continuation = GoogleEventWriter.ContinuationOf(seriesJson.RootElement, newStart, newEnd, recurrence,
                                                            edit with { Recurrence = null, Start = null, End = null });

        using var truncated = await api.SendAsync(HttpMethod.Patch, EventUrl(calendarId, series.Key.EventId) + Query(notifyGuests),
                                                  new JsonObject { ["recurrence"] = Lines(SeriesSplit.EndBefore(series.Recurrence, occurrenceStart)) },
                                                  cancellationToken);
        using var created = await api.SendAsync(HttpMethod.Post, EventsUrl(calendarId) + Query(notifyGuests), continuation, cancellationToken);
        return [ToChange(truncated!.RootElement, calendarId), ToChange(created!.RootElement, calendarId)];
    }

    public async Task<IReadOnlyList<EventChange>> DeleteEventAsync(
        EventTarget target, EditScope scope, bool notifyGuests, CancellationToken cancellationToken)
    {
        string calendarId = target.CalendarId;
        if (!target.IsRepeating || scope == EditScope.ThisEvent)
        {
            string id = TargetId(target);
            await api.SendAsync(HttpMethod.Delete, EventUrl(calendarId, id) + Query(notifyGuests, conference: false), null, cancellationToken);
            if (!target.IsRepeating)
            {
                return [new EventChange(id, null, null)];
            }

            // A deleted occurrence becomes a cancelled exception, which is what hides it from the series. It's written
            // here as the next sync would bring it, rather than asked for (Google answers 410 for deleted events).
            var cancelled = new JsonObject
            {
                ["id"] = id,
                ["status"] = "cancelled",
                ["recurringEventId"] = target.SeriesId,
                ["originalStartTime"] = GoogleEventWriter.Time(target.OriginalStart ?? target.OccurrenceStart),
            };
            using var payload = JsonDocument.Parse(cancelled.ToJsonString());
            return [ToChange(payload.RootElement, calendarId)];
        }

        using var seriesJson = await GetEventAsync(calendarId, target.SeriesId!, cancellationToken);
        var series = GoogleEventMapper.Map(seriesJson.RootElement, accountId, calendarId);
        var occurrenceStart = target.OriginalStart ?? target.OccurrenceStart;
        if (scope == EditScope.AllEvents || series.Start.OccurrenceKey == occurrenceStart.OccurrenceKey)
        {
            await api.SendAsync(HttpMethod.Delete, EventUrl(calendarId, series.Key.EventId) + Query(notifyGuests, conference: false), null, cancellationToken);
            return [new EventChange(series.Key.EventId, null, null)];
        }

        using var truncated = await api.SendAsync(HttpMethod.Patch, EventUrl(calendarId, series.Key.EventId) + Query(notifyGuests),
                                                  new JsonObject { ["recurrence"] = Lines(SeriesSplit.EndBefore(series.Recurrence, occurrenceStart)) },
                                                  cancellationToken);
        return [ToChange(truncated!.RootElement, calendarId)];
    }

    public async Task<IReadOnlyList<EventChange>> RespondAsync(EventTarget target, Rsvp rsvp, EditScope scope, CancellationToken cancellationToken)
    {
        string calendarId = target.CalendarId;
        string id = target.IsRepeating && scope != EditScope.ThisEvent ? target.SeriesId! : TargetId(target);

        // Google replaces the whole guest list on a patch, so send it back with only the user's own answer changed.
        using var current = await GetEventAsync(calendarId, id, cancellationToken);
        var attendees = new JsonArray();
        if (current.RootElement.TryGetProperty("attendees", out var list))
        {
            foreach (var a in list.EnumerateArray())
            {
                var node = (JsonObject)JsonNode.Parse(a.GetRawText())!;
                if (GoogleEventMapper.Bool(a, "self"))
                {
                    node["responseStatus"] = rsvp.Response switch
                    {
                        AttendeeResponse.Accepted => "accepted",
                        AttendeeResponse.Declined => "declined",
                        AttendeeResponse.Tentative => "tentative",
                        _ => "needsAction",
                    };
                    if (rsvp.Comment is { Length: > 0 } comment)
                    {
                        node["comment"] = comment;
                    }
                    else
                    {
                        node.Remove("comment");
                    }
                }

                attendees.Add(node);
            }
        }

        using var updated = await api.SendAsync(HttpMethod.Patch, EventUrl(calendarId, id) + "?sendUpdates=all",
                                                new JsonObject { ["attendees"] = attendees }, cancellationToken);
        return [ToChange(updated!.RootElement, calendarId)];
    }

    public async Task<IReadOnlyDictionary<string, FreeBusy>> GetFreeBusyAsync(
        IReadOnlyList<string> emails, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["timeMin"] = from.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["timeMax"] = to.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["items"] = new JsonArray(emails.Select(e => (JsonNode)new JsonObject { ["id"] = e }).ToArray()),
        };
        using var response = await api.SendAsync(HttpMethod.Post, "freeBusy", body, cancellationToken);

        var result = new Dictionary<string, FreeBusy>(StringComparer.OrdinalIgnoreCase);
        if (response!.RootElement.TryGetProperty("calendars", out var calendars))
        {
            foreach (var person in calendars.EnumerateObject())
            {
                bool unknown = person.Value.TryGetProperty("errors", out var errors) && errors.GetArrayLength() > 0;
                var busy = person.Value.TryGetProperty("busy", out var periods)
                    ? periods.EnumerateArray()
                        .Select(p => (DateTimeOffset.Parse(p.GetProperty("start").GetString()!, CultureInfo.InvariantCulture),
                                      DateTimeOffset.Parse(p.GetProperty("end").GetString()!, CultureInfo.InvariantCulture)))
                        .ToList()
                    : [];
                result[person.Name] = new FreeBusy(busy, unknown);
            }
        }

        return result;
    }

    /// <summary>
    /// Google's ID for the target: the event's own ID, or for an unchanged occurrence of a series
    /// "series_20261106T090000Z" (all-day: "series_20261106"). A series split off by "this and following" is named
    /// "series_R20261105T170000", but its occurrences keep the original name.
    /// </summary>
    internal static string TargetId(EventTarget target)
    {
        if (!target.Event.IsSeries || target.OriginalStart is not { } original)
        {
            return target.Event.Key.EventId;
        }

        string baseId = System.Text.RegularExpressions.Regex.Replace(target.Event.Key.EventId, @"_R\d{8}(T\d{6})?$", string.Empty);
        string when = original.IsAllDay
            ? original.Date!.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            : original.DateTime!.Value.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        return $"{baseId}_{when}";
    }

    /// <summary>Where an occurrence sits in the series' pattern: its original start, and the series' length.</summary>
    internal static (EventTime Start, EventTime End) Slot(CalendarEvent series, EventTime originalStart)
    {
        if (originalStart.IsAllDay)
        {
            int days = series.Start.Date is { } first && series.End.Date is { } last ? Math.Max(1, last.DayNumber - first.DayNumber) : 1;
            return (originalStart, EventTime.AllDay(originalStart.Date!.Value.AddDays(days)));
        }

        var length = series.End.EarliestUtc - series.Start.EarliestUtc;
        var start = EventTime.At(originalStart.DateTime!.Value, originalStart.TimeZone ?? series.Start.TimeZone);
        return (start, EventTime.At(start.DateTime!.Value + length, start.TimeZone));
    }

    private static EventTime WithZone(EventTime time, string? zone) =>
        time.IsAllDay || time.TimeZone is not null || zone is null ? time : EventTime.At(time.DateTime!.Value, zone);

    private async Task<JsonDocument> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken) =>
        await api.GetAsync(EventUrl(calendarId, eventId), cancellationToken);

    private static string EventsUrl(string calendarId) => $"calendars/{Uri.EscapeDataString(calendarId)}/events";

    private static string EventUrl(string calendarId, string eventId) => $"{EventsUrl(calendarId)}/{Uri.EscapeDataString(eventId)}";

    private static string Query(bool notifyGuests, bool conference = true) =>
        $"?sendUpdates={(notifyGuests ? "all" : "none")}{(conference ? "&conferenceDataVersion=1" : string.Empty)}";

    private static JsonArray Lines(IEnumerable<string> lines) => new(lines.Select(l => (JsonNode)l).ToArray());

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

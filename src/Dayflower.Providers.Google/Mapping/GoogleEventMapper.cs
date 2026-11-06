// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using System.Text.Json;
using Dayflower.Core.Model;

namespace Dayflower.Providers.Google.Mapping;

/// <summary>Maps a Google Calendar API event resource to <see cref="CalendarEvent"/>.</summary>
internal static class GoogleEventMapper
{
    public static CalendarEvent Map(JsonElement e, string accountId, string calendarId)
    {
        var originalStart = ReadTime(e, "originalStartTime");

        // A cancelled occurrence of a series may come back with nothing but its ID, series and original start.
        var start = ReadTime(e, "start") ?? originalStart
            ?? throw new FormatException($"Event {Str(e, "id")} has no start.");
        var end = ReadTime(e, "end") ?? start;

        return new CalendarEvent
        {
            Key = new EventKey(accountId, calendarId, Str(e, "id") ?? throw new FormatException("Event without an ID.")),
            ICalUid = Str(e, "iCalUID"),
            Status = Str(e, "status") switch
            {
                "tentative" => EventStatus.Tentative,
                "cancelled" => EventStatus.Cancelled,
                _ => EventStatus.Confirmed,
            },
            Summary = Str(e, "summary"),
            Location = Str(e, "location"),
            Description = Str(e, "description"),
            Start = start,
            End = end,
            Recurrence = e.TryGetProperty("recurrence", out var rec) && rec.ValueKind == JsonValueKind.Array
                ? rec.EnumerateArray().Select(r => r.GetString()!).Where(r => !string.IsNullOrWhiteSpace(r)).ToList()
                : [],
            RecurringEventId = Str(e, "recurringEventId"),
            OriginalStart = originalStart,
            Organizer = e.TryGetProperty("organizer", out var org)
                ? new EventPerson(Str(org, "email"), Str(org, "displayName"), Bool(org, "self"))
                : null,
            Attendees = e.TryGetProperty("attendees", out var att) && att.ValueKind == JsonValueKind.Array
                ? att.EnumerateArray().Select(MapAttendee).ToList()
                : [],
            Reminders = MapReminders(e),
            Conference = MapConference(e),
            HangoutLink = Str(e, "hangoutLink"),
            ColorId = Str(e, "colorId"),
            IsBusy = Str(e, "transparency") != "transparent",
            Visibility = Str(e, "visibility") switch
            {
                "public" => EventVisibility.Public,
                "private" => EventVisibility.Private,
                "confidential" => EventVisibility.Confidential,
                _ => EventVisibility.Default,
            },
            Kind = Str(e, "eventType") switch
            {
                "outOfOffice" => EventKind.OutOfOffice,
                "focusTime" => EventKind.FocusTime,
                "workingLocation" => EventKind.WorkingLocation,
                "birthday" => EventKind.Birthday,
                "fromGmail" => EventKind.FromGmail,
                _ => EventKind.Default,
            },
            WebLink = Str(e, "htmlLink"),
            Updated = Str(e, "updated") is { } updated ? ParseDateTime(updated) : null,
        };
    }

    private static Attendee MapAttendee(JsonElement a) => new(
        Str(a, "email"),
        Str(a, "displayName"),
        Str(a, "responseStatus") switch
        {
            "accepted" => AttendeeResponse.Accepted,
            "declined" => AttendeeResponse.Declined,
            "tentative" => AttendeeResponse.Tentative,
            _ => AttendeeResponse.NeedsAction,
        },
        Bool(a, "self"),
        Bool(a, "organizer"),
        Bool(a, "optional"),
        Bool(a, "resource"),
        Str(a, "comment"));

    private static ReminderSettings MapReminders(JsonElement e)
    {
        if (!e.TryGetProperty("reminders", out var r))
        {
            return ReminderSettings.Default;
        }

        var overrides = r.TryGetProperty("overrides", out var o) && o.ValueKind == JsonValueKind.Array
            ? o.EnumerateArray().Select(MapReminder).ToList()
            : [];
        return new ReminderSettings(Bool(r, "useDefault"), overrides);
    }

    internal static Reminder MapReminder(JsonElement r) => new(
        Str(r, "method") == "email" ? ReminderMethod.Email : ReminderMethod.Popup,
        r.TryGetProperty("minutes", out var m) ? m.GetInt32() : 0);

    private static ConferenceInfo? MapConference(JsonElement e)
    {
        if (!e.TryGetProperty("conferenceData", out var c))
        {
            return null;
        }

        string? solution = c.TryGetProperty("conferenceSolution", out var s) ? Str(s, "name") : null;
        var entryPoints = c.TryGetProperty("entryPoints", out var eps) && eps.ValueKind == JsonValueKind.Array
            ? eps.EnumerateArray()
                .Where(ep => Str(ep, "uri") is not null)
                .Select(ep => new ConferenceEntryPoint(Str(ep, "entryPointType") ?? "more", Str(ep, "uri")!, Str(ep, "label")))
                .ToList()
            : [];
        return new ConferenceInfo(solution, entryPoints);
    }

    private static EventTime? ReadTime(JsonElement e, string property)
    {
        if (!e.TryGetProperty(property, out var t) || t.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (Str(t, "date") is { } date)
        {
            return EventTime.AllDay(DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return Str(t, "dateTime") is { } dateTime ? EventTime.At(ParseDateTime(dateTime), Str(t, "timeZone")) : null;
    }

    private static DateTimeOffset ParseDateTime(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    internal static string? Str(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static bool Bool(JsonElement e, string property) =>
        e.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.True;
}

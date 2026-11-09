// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dayflower.Core.Editing;
using Dayflower.Core.Model;

namespace Dayflower.Providers.Google.Mapping;

/// <summary>Turns an <see cref="EventEdit"/> into the JSON body of a Google Calendar insert or patch.</summary>
internal static class GoogleEventWriter
{
    /// <summary>Fields of an event resource Google sets itself, dropped when an event is copied into a new one.</summary>
    private static readonly string[] ServerFields =
        ["kind", "etag", "id", "htmlLink", "created", "updated", "iCalUID", "sequence", "recurringEventId",
         "originalStartTime", "creator", "organizer", "hangoutLink", "privateCopy", "locked"];

    /// <param name="current">The event as it is now, so guests keep their answers when the guest list changes.</param>
    public static JsonObject Build(EventEdit edit, JsonElement? current = null)
    {
        var body = new JsonObject();
        Apply(body, edit, current);
        return body;
    }

    /// <summary>
    /// The body for the new series of a "this and following" split: the old series with Google's own fields removed,
    /// the given start, end and recurrence, and the edit on top. Guests, reminders and the meeting carry over.
    /// </summary>
    public static JsonObject ContinuationOf(JsonElement series, EventTime start, EventTime end, IReadOnlyList<string> recurrence, EventEdit edit)
    {
        var body = (JsonObject)JsonNode.Parse(series.GetRawText())!;
        foreach (var field in ServerFields)
        {
            body.Remove(field);
        }

        body["start"] = Time(start);
        body["end"] = Time(end);
        body["recurrence"] = Lines(recurrence);
        Apply(body, edit, series);
        return body;
    }

    private static void Apply(JsonObject body, EventEdit edit, JsonElement? current)
    {
        if (edit.Summary is not null) body["summary"] = edit.Summary;
        if (edit.Location is not null) body["location"] = edit.Location;
        if (edit.Description is not null) body["description"] = edit.Description;
        if (edit.Start is not null) body["start"] = Time(edit.Start);
        if (edit.End is not null) body["end"] = Time(edit.End);
        if (edit.Recurrence is not null) body["recurrence"] = Lines(edit.Recurrence);
        if (edit.Guests is not null) body["attendees"] = Attendees(edit.Guests, current);
        if (edit.GuestsCanModify is { } modify) body["guestsCanModify"] = modify;
        if (edit.GuestsCanInviteOthers is { } invite) body["guestsCanInviteOthers"] = invite;
        if (edit.GuestsCanSeeOtherGuests is { } see) body["guestsCanSeeOtherGuests"] = see;

        if (edit.Meet == true && !HasMeet(current))
        {
            body["conferenceData"] = new JsonObject
            {
                ["createRequest"] = new JsonObject
                {
                    ["requestId"] = Guid.NewGuid().ToString("N"),
                    ["conferenceSolutionKey"] = new JsonObject { ["type"] = "hangoutsMeet" },
                },
            };
        }
        else if (edit.Meet == false)
        {
            body["conferenceData"] = null;
        }

        if (edit.Reminders is { } reminders)
        {
            var overrides = new JsonArray();
            if (!reminders.UseDefault)
            {
                foreach (var r in reminders.Overrides)
                {
                    overrides.Add(new JsonObject { ["method"] = r.Method == ReminderMethod.Email ? "email" : "popup", ["minutes"] = r.MinutesBefore });
                }
            }

            body["reminders"] = new JsonObject { ["useDefault"] = reminders.UseDefault, ["overrides"] = overrides };
        }

        if (edit.ColorId is not null) body["colorId"] = edit.ColorId.Length == 0 ? null : edit.ColorId;
        if (edit.IsBusy is { } busy) body["transparency"] = busy ? "opaque" : "transparent";
        if (edit.Visibility is { } visibility) body["visibility"] = visibility.ToString().ToLowerInvariant();

        switch (edit.Kind)
        {
            case EventKind.OutOfOffice:
                body["eventType"] = "outOfOffice";
                body["transparency"] = "opaque";
                body["outOfOfficeProperties"] = DeclineProperties(edit.Workspace);
                break;
            case EventKind.FocusTime:
                body["eventType"] = "focusTime";
                body["transparency"] = "opaque";
                var focus = DeclineProperties(edit.Workspace);
                focus["chatStatus"] = "doNotDisturb";
                body["focusTimeProperties"] = focus;
                break;
            case EventKind.WorkingLocation:
                body["eventType"] = "workingLocation";
                body["visibility"] = "public";
                body["transparency"] = "transparent";
                body["workingLocationProperties"] = WorkingLocation(edit.Workspace);
                break;
        }
    }

    internal static JsonObject Time(EventTime time) => time.IsAllDay
        ? new JsonObject
        {
            ["date"] = time.Date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["dateTime"] = null,
            ["timeZone"] = null,
        }
        : new JsonObject
        {
            ["dateTime"] = time.DateTime!.Value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
            ["timeZone"] = time.TimeZone,
            ["date"] = null,
        };

    private static JsonArray Lines(IReadOnlyList<string> lines) => new(lines.Select(l => (JsonNode)l).ToArray());

    /// <summary>The guest list; guests already invited keep their answer, and the user and rooms stay as they are.</summary>
    private static JsonArray Attendees(IReadOnlyList<GuestEdit> guests, JsonElement? current)
    {
        var existing = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
        var kept = new JsonArray();
        if (current is { } e && e.TryGetProperty("attendees", out var attendees) && attendees.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in attendees.EnumerateArray())
            {
                var node = JsonNode.Parse(a.GetRawText())!;
                if (GoogleEventMapper.Bool(a, "self") || GoogleEventMapper.Bool(a, "resource"))
                {
                    kept.Add(node);
                }
                else if (GoogleEventMapper.Str(a, "email") is { } email)
                {
                    existing[email] = node;
                }
            }
        }

        foreach (var guest in guests.DistinctBy(g => g.Email, StringComparer.OrdinalIgnoreCase))
        {
            var node = existing.TryGetValue(guest.Email, out var known) ? known : new JsonObject { ["email"] = guest.Email };
            if (guest.IsOptional)
            {
                node["optional"] = true;
            }
            else
            {
                ((JsonObject)node).Remove("optional");
            }

            kept.Add(node);
        }

        return kept;
    }

    private static bool HasMeet(JsonElement? current) =>
        current is { } e && e.TryGetProperty("conferenceData", out var c) && c.ValueKind == JsonValueKind.Object;

    private static JsonObject DeclineProperties(WorkspaceDetails? details) => new()
    {
        ["autoDeclineMode"] = (details?.AutoDecline ?? AutoDeclineMode.None) switch
        {
            AutoDeclineMode.AllConflicting => "declineAllConflictingInvitations",
            AutoDeclineMode.NewInvitations => "declineOnlyNewConflictingInvitations",
            _ => "declineNone",
        },
        ["declineMessage"] = details?.DeclineMessage ?? string.Empty,
    };

    private static JsonObject WorkingLocation(WorkspaceDetails? details)
    {
        var label = details?.LocationLabel ?? string.Empty;
        return (details?.Location ?? WorkingLocationType.Home) switch
        {
            WorkingLocationType.Office => new JsonObject { ["type"] = "officeLocation", ["officeLocation"] = new JsonObject { ["label"] = label } },
            WorkingLocationType.Custom => new JsonObject { ["type"] = "customLocation", ["customLocation"] = new JsonObject { ["label"] = label } },
            _ => new JsonObject { ["type"] = "homeOffice", ["homeOffice"] = new JsonObject() },
        };
    }
}

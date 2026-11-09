// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Text.Json;
using Dayflower.Core.Model;
using static Dayflower.Providers.Google.Mapping.GoogleEventMapper;

namespace Dayflower.Providers.Google.Mapping;

/// <summary>Maps a Google Calendar API calendarList entry to <see cref="CalendarRecord"/>.</summary>
internal static class GoogleCalendarMapper
{
    /// <summary>Whether the entry should be dropped: deleted, or hidden from the user's calendar list.</summary>
    public static bool IsRemoved(JsonElement entry) => Bool(entry, "deleted") || Bool(entry, "hidden");

    public static CalendarRecord Map(JsonElement entry, string accountId) => new(
        accountId,
        Str(entry, "id") ?? throw new FormatException("Calendar without an ID."),
        Str(entry, "summaryOverride") ?? Str(entry, "summary") ?? Str(entry, "id")!,
        Str(entry, "timeZone"),
        Str(entry, "backgroundColor"),
        Str(entry, "foregroundColor"),
        Str(entry, "accessRole") switch
        {
            "owner" => CalendarAccess.Owner,
            "writer" => CalendarAccess.Writer,
            "freeBusyReader" => CalendarAccess.FreeBusyReader,
            _ => CalendarAccess.Reader,
        },
        Bool(entry, "primary"),
        Bool(entry, "selected"),
        entry.TryGetProperty("defaultReminders", out var reminders) && reminders.ValueKind == JsonValueKind.Array
            ? reminders.EnumerateArray().Select(MapReminder).ToList()
            : []);
}

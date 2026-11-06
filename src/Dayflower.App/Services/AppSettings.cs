// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dayflower.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>User preferences, kept in settings.json next to the cache. The Settings page edits them (phase 6).</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Hours the day and week views start and end shading as outside working time.</summary>
    public int WorkdayStartHour { get; set; } = 8;

    public int WorkdayEndHour { get; set; } = 18;

    /// <summary>How many minutes before a meeting starts its Join button appears on the event in the grid.</summary>
    public int JoinLeadMinutes { get; set; } = 10;

    /// <summary>A second notification, with Join, when a meeting starts.</summary>
    public bool StartingNowAlert { get; set; } = true;

    /// <summary>Reminders stay on screen until dealt with (Windows' reminder style), rather than fading away.</summary>
    public bool KeepRemindersOnScreen { get; set; } = true;

    /// <summary>The calendar the last new event went to ("accountId|calendarId"), to suggest for the next one.</summary>
    public string? LastCalendar { get; set; }

    /// <summary>The calendar new events go to ("accountId|calendarId"); null means the last one used.</summary>
    public string? DefaultCalendar { get; set; }

    /// <summary>Show invitations the user declined (struck through), as Google Calendar's "Show declined events".</summary>
    public bool ShowDeclinedEvents { get; set; }

    /// <summary>A notification with Accept / Maybe / Decline when a new invitation arrives.</summary>
    public bool InvitationNotifications { get; set; } = true;

    /// <summary>The world clock's time zones, in the order shown.</summary>
    public List<WorldClockEntry> WorldClocks { get; set; } = [];

    /// <summary>Raised when the Settings page changes something the rest of the app shows.</summary>
    public event EventHandler? Changed;

    /// <summary>Saves and tells the app to apply the change.</summary>
    public void SaveAndApply()
    {
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Whether the "still running in the background" notice has been shown once.</summary>
    public bool BackgroundNoticeShown { get; set; }

    private static string FilePath => Path.Combine(AppPaths.DataFolder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new AppSettings();
        }
    }

    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
}

/// <summary>One world clock row: an IANA time zone and the user's name for it.</summary>
public sealed class WorldClockEntry
{
    public string ZoneId { get; set; } = "Etc/UTC";

    public string Label { get; set; } = string.Empty;
}

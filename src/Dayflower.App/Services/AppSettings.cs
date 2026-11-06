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

    /// <summary>Whether the "still running in the tray" notice has been shown once.</summary>
    public bool TrayNoticeShown { get; set; }

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

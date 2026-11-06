// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using Microsoft.Win32;
using Windows.ApplicationModel;

namespace Dayflower.Services;

/// <summary>
/// Starting with Windows. As an MSIX package, through the package's startup task (declared in the manifest, on by
/// default). As a plain program, through a Run entry for the current user that starts Dayflower hidden with
/// <see cref="BackgroundArgument"/>; the installer adds it. Either way, turning it off in Task Manager or Windows
/// Settings (Apps › Startup) wins, and Dayflower can't turn it back on.
/// </summary>
public static class StartupService
{
    /// <summary>On the command line when Windows starts Dayflower at sign-in: it starts without showing its window.</summary>
    public const string BackgroundArgument = "--background";

    private const string TaskId = "DayflowerStartup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>The Run entry's name: "Dayflower", or a test install's own app ID.</summary>
    private static string EntryName => AppIdentity.Name;

    /// <summary>Whether Dayflower starts with Windows, or null when that can't be read.</summary>
    public static async Task<StartupTaskState?> GetStateAsync()
    {
        try
        {
            if (AppIdentity.IsPackaged)
            {
                return (await StartupTask.GetAsync(TaskId)).State;
            }

            if (!RunEntryIsThisCopy())
            {
                return StartupTaskState.Disabled;
            }

            return TurnedOffInWindows() ? StartupTaskState.DisabledByUser : StartupTaskState.Enabled;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<StartupTaskState?> SetEnabledAsync(bool enabled)
    {
        if (AppIdentity.IsPackaged)
        {
            var task = await StartupTask.GetAsync(TaskId);
            if (enabled)
            {
                return await task.RequestEnableAsync();
            }

            task.Disable();
            return task.State;
        }

        if (enabled)
        {
            AddRunEntry(EntryName);
        }
        else
        {
            RemoveRunEntry(EntryName);
        }

        return await GetStateAsync();
    }

    /// <summary>Adds a Run entry, under the given name, that starts this copy of Dayflower (a plain program only).</summary>
    public static void AddRunEntry(string name)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        run.SetValue(name, $"\"{Environment.ProcessPath}\" {BackgroundArgument}");
    }

    /// <summary>Removes the Run entry of the given name (a plain program only).</summary>
    public static void RemoveRunEntry(string name)
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        run?.DeleteValue(name, throwOnMissingValue: false);
    }

    /// <summary>
    /// Whether the Run entry starts this copy. Another copy (a development build, say) may have written its own path.
    /// </summary>
    private static bool RunEntryIsThisCopy()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        if (run?.GetValue(EntryName) is not string command)
        {
            return false;
        }

        string path = command.StartsWith('"') ? command[1..command.IndexOf('"', 1)] : command.Split(' ')[0];
        return string.Equals(Path.GetFullPath(path), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the entry is switched off in Task Manager or Windows Settings, which keep their own record: the first
    /// byte of the entry's value is odd when it's off.
    /// </summary>
    private static bool TurnedOffInWindows()
    {
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return approved?.GetValue(EntryName) is byte[] { Length: > 0 } state && (state[0] & 1) == 1;
    }
}

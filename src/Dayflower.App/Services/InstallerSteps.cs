// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Diagnostics;
using Microsoft.Windows.AppNotifications;

namespace Dayflower.Services;

/// <summary>
/// What Dayflower's installer (Velopack) runs at install and uninstall. Each runs in a short-lived copy of Dayflower,
/// started by the installer, that exits straight after; it may show no UI and has 30 seconds.
/// </summary>
public static class InstallerSteps
{
    /// <summary>Starts with Windows by default, as the MSIX package did.</summary>
    public static void AfterInstall()
    {
        if (AppIdentity.FindInstalledAppId() is { } id)
        {
            StartupService.AddRunEntry(id);
        }
    }

    /// <summary>
    /// Leaves nothing behind, as removing the MSIX package did: the start-up entry, the notification registration, and
    /// the data folder with the cache, settings and saved sign-ins. Without a usable app ID it deletes nothing.
    /// </summary>
    public static void BeforeUninstall()
    {
        if (AppIdentity.FindInstalledAppId() is not { } id)
        {
            return;
        }

        // Dayflower is usually running in the background, and the installer doesn't stop it: it would keep the cache
        // open and its own files in use, and both folders would stay behind.
        StopRunningCopies();
        StartupService.RemoveRunEntry(id);
        try
        {
            AppNotificationManager.Default.UnregisterAll();
        }
        catch (Exception)
        {
            // Nothing registered.
        }

        try
        {
            string icons = TaskbarDate.TempFolderFor(id);
            if (Directory.Exists(icons))
            {
                Directory.Delete(icons, recursive: true);
            }
        }
        catch (IOException)
        {
            // Left for Windows' own Temp cleanup.
        }

        string data = AppPaths.DataFolderFor(id);
        for (int attempt = 0; attempt < 5 && Directory.Exists(data); attempt++)
        {
            try
            {
                Directory.Delete(data, recursive: true);
            }
            catch (IOException)
            {
                Thread.Sleep(500); // a stopped copy's files take a moment to be released
            }
        }
    }

    /// <summary>Ends every other running copy of this same install (the same Dayflower.exe as this step's).</summary>
    private static void StopRunningCopies()
    {
        foreach (var process in Process.GetProcessesByName("Dayflower"))
        {
            try
            {
                if (process.Id != Environment.ProcessId
                    && string.Equals(process.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
            }
            catch (Exception)
            {
                // Already gone, or not ours to stop.
            }
            finally
            {
                process.Dispose();
            }
        }
    }
}

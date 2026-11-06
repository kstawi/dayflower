// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

namespace Dayflower.Services;

/// <summary>Where Dayflower keeps its cache and logs.</summary>
public static class AppPaths
{
    /// <summary>
    /// The package's LocalState folder when installed as a package (removed on uninstall), otherwise
    /// %LOCALAPPDATA%\Dayflower.
    /// </summary>
    public static string DataFolder { get; } = Resolve();

    public static string Database => Path.Combine(DataFolder, "dayflower.db");

    public static string LogsFolder => Path.Combine(DataFolder, "logs");

    private static string Resolve()
    {
        string folder;
        try
        {
            folder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch (Exception)
        {
            // No package identity (an unpackaged build).
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dayflower");
        }

        Directory.CreateDirectory(folder);
        return folder;
    }
}

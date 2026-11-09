// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

namespace Dayflower.Services;

/// <summary>Where Dayflower keeps its cache, settings, saved sign-ins and logs.</summary>
public static class AppPaths
{
    /// <summary>
    /// The package's LocalState folder when installed as an MSIX package (removed on uninstall); otherwise
    /// %AppData%\Dayflower (see <see cref="DataFolderFor"/>).
    /// </summary>
    public static string DataFolder { get; } = Resolve();

    public static string Database => Path.Combine(DataFolder, "dayflower.db");

    public static string LogsFolder => Path.Combine(DataFolder, "logs");

    /// <summary>
    /// %AppData%\{name}: outside the installer's folder (%LocalAppData%\Dayflower), which it replaces in part on every
    /// update and deletes on uninstall. Dayflower's uninstall step deletes this folder itself.
    /// </summary>
    public static string DataFolderFor(string name) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), name);

    private static string Resolve()
    {
        string folder = AppIdentity.IsPackaged
            ? Windows.Storage.ApplicationData.Current.LocalFolder.Path
            : DataFolderFor(AppIdentity.Name);
        Directory.CreateDirectory(folder);
        return folder;
    }
}

// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Runtime.InteropServices;
using Velopack.Locators;

namespace Dayflower.Services;

/// <summary>
/// How this copy of Dayflower is installed: as an MSIX package, by Dayflower's installer (Velopack), or not at all (a
/// development build). A few Windows features work differently between them: starting with Windows, updates, the
/// version number and where data is kept.
/// </summary>
public static partial class AppIdentity
{
    public const string DefaultName = "Dayflower";

    private const int NoPackage = 15700; // APPMODEL_ERROR_NO_PACKAGE

    public static bool IsPackaged { get; } = HasPackage();

    /// <summary>The installer's app ID when the installer put this copy here, otherwise null.</summary>
    public static string? InstalledAppId { get; } = FindInstalledAppId();

    /// <summary>
    /// The name of this copy's data folder and start-up entry: the installer's app ID ("Dayflower" for the real thing),
    /// so a test install under another ID never touches the real data. Development builds use "Dayflower" too, and so
    /// share data with an installed copy.
    /// </summary>
    public static string Name => InstalledAppId ?? DefaultName;

    /// <summary>
    /// The installer's app ID, or null when this copy wasn't installed by it. Only an ID that is safe as a folder name
    /// counts, since the uninstaller deletes the data folder named after it.
    /// </summary>
    public static string? FindInstalledAppId()
    {
        try
        {
            var locator = VelopackLocator.Current;
            return locator.CurrentlyInstalledVersion is not null
                   && locator.AppId is { Length: > 0 } id
                   && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_')
                ? id
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool HasPackage()
    {
        uint length = 0;
        return GetCurrentPackageFullName(ref length, IntPtr.Zero) != NoPackage;
    }

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint length, IntPtr fullName);
}

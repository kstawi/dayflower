// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Velopack.Locators;

namespace Dayflower.Services;

/// <summary>
/// Puts today's date on Dayflower's taskbar button, like the Calendar icon on a Mac.
/// <para>
/// Windows 11 draws a window's taskbar button from the Start menu shortcut that shares the window's taskbar identity
/// (app ID), and ignores the window's own icon. So an installed copy points its Start menu shortcut, and any pinned copy
/// of it, at today's icon (a copy the taskbar can read, see <see cref="ReadableCopy"/>); that also dates the Start menu
/// entry. The taskbar reads a shortcut's icon only when it creates a button, so when the date changes while the window
/// is open, the window briefly takes another app ID and then its own again, and the taskbar makes the button afresh.
/// (Found by trying: re-setting the same app ID does nothing.)
/// </para>
/// <para>
/// A development build has no such shortcut: its window gets an app ID of its own that no shortcut carries, and the
/// taskbar then shows the window's own (dated) icon. The MSIX package's button always shows the package logo.
/// </para>
/// </summary>
public static partial class TaskbarDate
{
    private const string DevelopmentAppId = "kstawi.Dayflower.Development";

    private static readonly Guid AppIdKey = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"); // PKEY_AppUserModel_ID, pid 5

    /// <summary>
    /// Shows <paramref name="icon"/> (today's) on the window's taskbar button. <paramref name="redraw"/> makes the
    /// taskbar redraw a button it has already made, for when the date changes while the window is open.
    /// </summary>
    public static async Task ShowAsync(IntPtr window, string icon, bool redraw)
    {
        if (AppIdentity.IsPackaged || AppId() is not { } appId)
        {
            return;
        }

        bool changed = false;
        if (AppIdentity.InstalledAppId is not null && ReadableCopy(icon) is { } copy)
        {
            foreach (string shortcut in OurShortcuts())
            {
                changed |= SetShortcutIcon(shortcut, copy);
            }
        }

        if (redraw)
        {
            await RedrawAsync(window, appId);
        }
        else
        {
            SetWindowAppId(window, appId);
            if (changed)
            {
                // The button may be made while Windows is still taking in the changed shortcut.
                await Task.Delay(TimeSpan.FromSeconds(3));
                await RedrawAsync(window, appId);
            }
        }
    }

    /// <summary>
    /// Keeps today's icon where the taskbar can read it, and checks it's still there (a cleanup of the Temp folder may
    /// remove it). The taskbar can't read icons in a folder made under AppData since Windows started (seen on Windows 11
    /// build 26300, the installed copy's own folder included), but it can in the Temp folder, which always exists.
    /// </summary>
    public static string? ReadableCopy(string icon)
    {
        try
        {
            string folder = TempFolder();
            string copy = Path.Combine(folder, Path.GetFileName(icon));
            if (!File.Exists(copy))
            {
                Directory.CreateDirectory(folder);
                foreach (string old in Directory.EnumerateFiles(folder, "day-*.ico"))
                {
                    File.Delete(old);
                }

                File.Copy(icon, copy);
            }

            return copy;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>%TEMP%\{name}, where today's icon is kept; removed on uninstall.</summary>
    public static string TempFolder() => TempFolderFor(AppIdentity.Name);

    public static string TempFolderFor(string name) => Path.Combine(Path.GetTempPath(), name);

    private static async Task RedrawAsync(IntPtr window, string appId)
    {
        SetWindowAppId(window, appId + ".Redraw");
        await Task.Delay(300);
        SetWindowAppId(window, appId);
    }

    /// <summary>The installed copy's app ID (Velopack's, which its shortcut carries), or the development build's.</summary>
    private static string? AppId()
    {
        if (AppIdentity.InstalledAppId is null)
        {
            return DevelopmentAppId;
        }

        try
        {
            return VelopackLocator.Current.AppUserModelId;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The Start menu shortcuts and pinned taskbar shortcuts that start this copy's Dayflower.exe.</summary>
    private static IEnumerable<string> OurShortcuts()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] folders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"),
        ];
        foreach (string folder in folders.Where(Directory.Exists))
        {
            foreach (string shortcut in Directory.EnumerateFiles(folder, "*.lnk"))
            {
                if (string.Equals(ShortcutTarget(shortcut), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                {
                    yield return shortcut;
                }
            }
        }
    }

    private static string? ShortcutTarget(string shortcut)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(shortcut, 0);
            char[] path = new char[1024];
            link.GetPath(path, path.Length, IntPtr.Zero, 0);
            return new string(path, 0, Array.IndexOf(path, '\0') is var end and >= 0 ? end : path.Length);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>Points the shortcut at the icon, if it isn't already; returns whether it changed.</summary>
    private static bool SetShortcutIcon(string shortcut, string icon)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            var file = (IPersistFile)link;
            file.Load(shortcut, 2); // STGM_READWRITE
            char[] current = new char[1024];
            link.GetIconLocation(current, current.Length, out _);
            if (string.Equals(new string(current).TrimEnd('\0'), icon, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            link.SetIconLocation(icon, 0);
            file.Save(shortcut, true);
            SHChangeNotify(0x00002000, 0x0005, shortcut, IntPtr.Zero); // SHCNE_UPDATEITEM, SHCNF_PATHW
            return true;
        }
        catch (Exception)
        {
            return false; // A shortcut that can't be changed keeps its icon.
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    private static void SetWindowAppId(IntPtr window, string appId)
    {
        try
        {
            SHGetPropertyStoreForWindow(window, typeof(IPropertyStore).GUID, out var store);
            var key = new PropertyKey { FormatId = AppIdKey, PropertyId = 5 };
            var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(appId) }; // VT_LPWSTR
            try
            {
                store.SetValue(ref key, ref value);
            }
            finally
            {
                Marshal.FreeCoTaskMem(value.Pointer);
                Marshal.ReleaseComObject(store);
            }
        }
        catch (Exception)
        {
            // The window keeps the process's app ID.
        }
    }

    [DllImport("shell32.dll", PreserveSig = false)]
    private static extern void SHGetPropertyStoreForWindow(IntPtr window, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IPropertyStore store);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial void SHChangeNotify(int eventId, uint flags, string item1, IntPtr item2);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort Type;

        [FieldOffset(8)]
        public IntPtr Pointer;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint count);

        void GetAt(uint index, out PropertyKey key);

        void GetValue(ref PropertyKey key, out PropVariant value);

        void SetValue(ref PropertyKey key, ref PropVariant value);

        void Commit();
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] file, int size, IntPtr findData, uint flags);

        void GetIDList(out IntPtr idList);

        void SetIDList(IntPtr idList);

        void GetDescription([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] name, int size);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] dir, int size);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);

        void GetArguments([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] args, int size);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCmd);

        void SetShowCmd(int showCmd);

        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] path, int size, out int index);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);

        void Resolve(IntPtr window, uint flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}

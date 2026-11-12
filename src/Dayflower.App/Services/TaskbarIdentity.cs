// Dayflower - https://github.com/kstawi/dayflower
// Copyright (C) 2026 kstawi
// Licensed under the GNU General Public License v3.0; see the LICENSE file.

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Velopack.Locators;

namespace Dayflower.Services;

/// <summary>
/// Gives an installed copy's window the taskbar identity (app ID) its Start menu shortcut carries, however Dayflower
/// was started, so the window and a pinned button are one taskbar button with the shortcut's icon.
/// </summary>
public static partial class TaskbarIdentity
{
    private static readonly Guid AppIdKey = new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"); // PKEY_AppUserModel_ID, pid 5

    public static void Apply(IntPtr window)
    {
        if (AppIdentity.IsPackaged || AppIdentity.InstalledAppId is null)
        {
            return;
        }

        try
        {
            if (VelopackLocator.Current.AppUserModelId is { } appId)
            {
                SetWindowAppId(window, appId);
            }
        }
        catch (Exception)
        {
            // The window keeps the process's app ID.
        }

        // shortcut: betas up to 0.7.0-beta.4 pointed these shortcuts at a dated icon in a folder updates no longer
        // have; drop this once no copy that old is left.
        foreach (string shortcut in OurShortcuts())
        {
            ResetDatedIcon(shortcut);
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

    /// <summary>Points a shortcut that shows a dated icon (Assets\Days\day-N.ico) back at Dayflower.exe's own icon.</summary>
    private static void ResetDatedIcon(string shortcut)
    {
        var link = (IShellLinkW)new ShellLink();
        try
        {
            var file = (IPersistFile)link;
            file.Load(shortcut, 2); // STGM_READWRITE
            char[] current = new char[1024];
            link.GetIconLocation(current, current.Length, out _);
            if (!new string(current).TrimEnd('\0').Contains(@"\Assets\Days\", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            link.SetIconLocation(Environment.ProcessPath!, 0);
            file.Save(shortcut, true);
            SHChangeNotify(0x00002000, 0x0005, shortcut, IntPtr.Zero); // SHCNE_UPDATEITEM, SHCNF_PATHW
        }
        catch (Exception)
        {
            // A shortcut that can't be changed keeps its icon.
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    private static void SetWindowAppId(IntPtr window, string appId)
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

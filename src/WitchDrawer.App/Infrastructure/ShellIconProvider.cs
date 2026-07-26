using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WitchDrawer.App.Infrastructure;

public static class ShellIconProvider
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> IconTasks =
        new(StringComparer.OrdinalIgnoreCase);

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;
    private const int MaxPath = 260;
    private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");

    public static Task<ImageSource?> GetIconAsync(string? path, bool isDirectory, int size)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult<ImageSource?>(null);
        }

        var fullPath = Path.GetFullPath(path);
        var cacheKey = $"{(isDirectory ? "D" : "F")}|{size}|{fullPath}";
        var lazyTask = IconTasks.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<ImageSource?>>(
                () => LoadIconAsync(cacheKey, fullPath, isDirectory, size),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyTask.Value;
    }

    private static async Task<ImageSource?> LoadIconAsync(string cacheKey, string fullPath, bool isDirectory, int size)
    {
        try
        {
            var icon = await Task.Run(() => GetIcon(fullPath, isDirectory, size)).ConfigureAwait(false);
            if (icon is null)
            {
                IconTasks.TryRemove(cacheKey, out _);
            }

            return icon;
        }
        catch
        {
            IconTasks.TryRemove(cacheKey, out _);
            throw;
        }
    }

    private static ImageSource? GetIcon(string fullPath, bool isDirectory, int size)
    {
        if (!isDirectory && IsShortcut(fullPath))
        {
            foreach (var candidate in GetShortcutIconCandidates(fullPath))
            {
                var shortcutIcon = TryExtractCandidateIcon(candidate, size);
                if (shortcutIcon is not null)
                {
                    return shortcutIcon;
                }
            }
        }

        var attributes = isDirectory ? FileAttributeDirectory : FileAttributeNormal;
        var flags = GetIconFlags(size);

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            flags |= ShgfiUseFileAttributes;
        }

        return GetIcon(fullPath, attributes, flags, size)
            ?? GetIcon(fullPath, attributes, flags | ShgfiUseFileAttributes, size);
    }

    private static uint GetIconFlags(int size)
    {
        return ShgfiIcon | (size <= 20 ? ShgfiSmallIcon : ShgfiLargeIcon);
    }

    private static bool IsShortcut(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".url", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<IconCandidate> GetShortcutIconCandidates(string shortcutPath)
    {
        var descriptor = ShortcutDescriptor.TryLoad(shortcutPath);
        if (descriptor is null)
        {
            yield break;
        }

        var baseDir = Path.GetDirectoryName(shortcutPath) ?? string.Empty;

        if (TryParseIconLocation(descriptor.IconLocation, out var iconFile, out var iconIndex)
            && TryResolveIconPath(iconFile, baseDir, out var iconPath))
        {
            yield return new IconCandidate(iconPath, iconIndex);
        }

        if (TryGetExistingPath(descriptor.TargetPath, out var targetPath))
        {
            yield return new IconCandidate(targetPath, -1);
        }
    }

    private static ImageSource? TryExtractCandidateIcon(IconCandidate candidate, int size)
    {
        // A non-negative index means the candidate is an icon container
        // (.ico/.exe/.dll) and the specific icon must be extracted by index.
        // SHGetFileInfo would otherwise ignore the index and return icon #0,
        // which is often blank for icon-only libraries such as imageres.dll.
        if (candidate.IconIndex >= 0)
        {
            var indexed = TryExtractIndexedIcon(candidate.Path, candidate.IconIndex, size);
            if (indexed is not null)
            {
                return indexed;
            }
        }

        var attributes = Directory.Exists(candidate.Path) ? FileAttributeDirectory : FileAttributeNormal;
        var flags = GetIconFlags(size);

        if (!File.Exists(candidate.Path) && !Directory.Exists(candidate.Path))
        {
            flags |= ShgfiUseFileAttributes;
        }

        return GetIcon(candidate.Path, attributes, flags, size)
            ?? GetIcon(candidate.Path, attributes, flags | ShgfiUseFileAttributes, size);
    }

    private static ImageSource? TryExtractIndexedIcon(string path, int iconIndex, int size)
    {
        try
        {
            var handles = new nint[1];
            var extracted = PrivateExtractIcons(path, iconIndex, size, size, handles, null, 1, 0);
            if (extracted == 0 || handles[0] == nint.Zero)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    handles[0],
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(size, size));
                source.Freeze();
                return source;
            }
            finally
            {
                DestroyIcon(handles[0]);
            }
        }
        catch
        {
            return null;
        }
    }

    private static bool TryParseIconLocation(string? iconLocation, out string file, out int iconIndex)
        => ShortcutParsing.TryParseIconLocation(iconLocation, out file, out iconIndex);

    private static bool TryResolveIconPath(string rawPath, string baseDir, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return false;
        }

        var value = rawPath.Trim().Trim('"');

        try
        {
            // Resolve relative icon paths against the shortcut's own directory
            // first (common for .url files authored next to their icon).
            if (!Path.IsPathRooted(value) && !string.IsNullOrEmpty(baseDir))
            {
                var relative = Path.GetFullPath(Path.Combine(baseDir, value));
                if (File.Exists(relative))
                {
                    fullPath = relative;
                    return true;
                }
            }

            return TryGetExistingPath(value, out fullPath);
        }
        catch
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private static bool TryGetExistingPath(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            return File.Exists(fullPath) || Directory.Exists(fullPath);
        }
        catch
        {
            fullPath = string.Empty;
            return false;
        }
    }

    private static ImageSource? GetIcon(string fullPath, uint attributes, uint flags, int size)
    {
        var result = SHGetFileInfo(
            fullPath,
            attributes,
            out var info,
            (uint)Marshal.SizeOf<ShellFileInfo>(),
            flags);

        if (result == nint.Zero || info.IconHandle == nint.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(size, size));
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(info.IconHandle);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern nint SHGetFileInfo(
        string path,
        uint fileAttributes,
        out ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    // Extracts a specific icon by index from an icon container (.ico/.exe/.dll),
    // which SHGetFileInfo cannot do (it always returns icon #0). Used for shortcut
    // IconLocation values such as "C:\Windows\System32\imageres.dll,109".
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PrivateExtractIcons(
        string szFileName,
        int nIconIndex,
        int cxIcon,
        int cyIcon,
        [Out, MarshalAs(UnmanagedType.LPArray)] nint[] phicon,
        [Out, MarshalAs(UnmanagedType.LPArray)] int[]? piconid,
        uint nIcons,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(nint icon);

    private sealed record IconCandidate(string Path, int IconIndex);

    private sealed record ShortcutDescriptor(string TargetPath, string IconLocation)
    {
        public static ShortcutDescriptor? TryLoad(string shortcutPath)
        {
            if (shortcutPath.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            {
                return TryLoadUrlShortcut(shortcutPath);
            }

            return TryLoadLnkShortcut(shortcutPath);
        }

        // .url files are plain INI text ([InternetShortcut] URL= IconFile= IconIndex=),
        // not IShellLinkW payloads, so parse them directly instead of going through COM.
        private static ShortcutDescriptor? TryLoadUrlShortcut(string shortcutPath)
        {
            try
            {
                var content = File.ReadAllText(shortcutPath);
                if (!ShortcutParsing.TryParseUrlShortcut(content, out var parsed))
                {
                    return null;
                }

                return new ShortcutDescriptor(parsed.TargetUrl, parsed.IconLocation);
            }
            catch
            {
                return null;
            }
        }

        private static ShortcutDescriptor? TryLoadLnkShortcut(string shortcutPath)
        {
            IShellLinkW? shellLink = null;
            try
            {
                var shellLinkType = Type.GetTypeFromCLSID(ShellLinkClassId, throwOnError: false);
                if (shellLinkType is null)
                {
                    return null;
                }

                shellLink = Activator.CreateInstance(shellLinkType) as IShellLinkW;
                if (shellLink is null)
                {
                    return null;
                }

                var persistFile = (IPersistFile)shellLink;
                if (persistFile.Load(shortcutPath, 0) != 0)
                {
                    return null;
                }

                var targetPath = new StringBuilder(MaxPath);
                shellLink.GetPath(targetPath, targetPath.Capacity, nint.Zero, 0);

                var iconLocation = new StringBuilder(MaxPath);
                shellLink.GetIconLocation(iconLocation, iconLocation.Capacity, out _);

                return new ShortcutDescriptor(targetPath.ToString(), iconLocation.ToString());
            }
            catch
            {
                return null;
            }
            finally
            {
                if (shellLink is not null)
                {
                    Marshal.ReleaseComObject(shellLink);
                }
            }
        }
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        [PreserveSig]
        int GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cchMaxPath,
            nint pfd,
            uint fFlags);

        [PreserveSig]
        int GetIDList(out nint ppidl);

        [PreserveSig]
        int SetIDList(nint pidl);

        [PreserveSig]
        int GetDescription(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName,
            int cchMaxName);

        [PreserveSig]
        int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        [PreserveSig]
        int GetWorkingDirectory(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir,
            int cchMaxPath);

        [PreserveSig]
        int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        [PreserveSig]
        int GetArguments(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs,
            int cchMaxPath);

        [PreserveSig]
        int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        [PreserveSig]
        int GetHotkey(out short pwHotkey);

        [PreserveSig]
        int SetHotkey(short wHotkey);

        [PreserveSig]
        int GetShowCmd(out int piShowCmd);

        [PreserveSig]
        int SetShowCmd(int iShowCmd);

        [PreserveSig]
        int GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
            int cchIconPath,
            out int piIcon);

        [PreserveSig]
        int SetIconLocation(
            [MarshalAs(UnmanagedType.LPWStr)] string pszIconPath,
            int iIcon);

        [PreserveSig]
        int SetRelativePath(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPathRel,
            uint dwReserved);

        [PreserveSig]
        int Resolve(nint hwnd, uint fFlags);

        [PreserveSig]
        int SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        [PreserveSig]
        int GetClassID(out Guid pClassID);

        [PreserveSig]
        int IsDirty();

        [PreserveSig]
        int Load(
            [MarshalAs(UnmanagedType.LPWStr)] string pszFileName,
            uint dwMode);

        [PreserveSig]
        int Save(
            [MarshalAs(UnmanagedType.LPWStr)] string? pszFileName,
            bool fRemember);

        [PreserveSig]
        int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName);

        [PreserveSig]
        int GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string? ppszFileName);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public nint IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }
}


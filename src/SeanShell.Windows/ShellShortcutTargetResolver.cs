using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace SeanShell.Windows;

internal static class ShellShortcutTargetResolver
{
    private const int MaximumPath = 32_768;
    private static readonly Guid ShellLinkClassId =
        new("00021401-0000-0000-C000-000000000046");

    public static string? GetProcessName(string shortcutPath)
    {
        if (!Path.GetExtension(shortcutPath).Equals(
                ".lnk",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        IShellLinkW? shellLink = null;
        try
        {
            var shellLinkType = Type.GetTypeFromCLSID(
                ShellLinkClassId,
                throwOnError: true) ??
                throw new InvalidOperationException(
                    "Windows did not expose the Shell Link COM class.");
            shellLink = (IShellLinkW)Activator.CreateInstance(shellLinkType)!;
            ((IPersistFile)shellLink).Load(shortcutPath, 0);
            var target = new StringBuilder(MaximumPath);
            if (shellLink.GetPath(target, target.Capacity, 0, 0) != 0)
            {
                return null;
            }

            var expanded = Environment.ExpandEnvironmentVariables(
                target.ToString().Trim());
            return Path.GetExtension(expanded).Equals(
                ".exe",
                StringComparison.OrdinalIgnoreCase)
                ? Path.GetFileNameWithoutExtension(expanded)
                : null;
        }
        catch (COMException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        finally
        {
            if (shellLink is not null)
            {
                _ = Marshal.FinalReleaseComObject(shellLink);
            }
        }
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig]
        int GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file,
            int maximumPath,
            nint findData,
            uint flags);

        void GetIDList(out nint itemIdList);
        void SetIDList(nint itemIdList);
        void GetDescription(StringBuilder name, int maximumName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory(StringBuilder directory, int maximumPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments(StringBuilder arguments, int maximumPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCommand);
        void SetShowCmd(int showCommand);
        void GetIconLocation(StringBuilder iconPath, int maximumPath, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(nint windowHandle, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}

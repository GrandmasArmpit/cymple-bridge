using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace CympleBridge.Bridge;

/// <summary>
/// Removes shortcuts, the Add/Remove Programs entry and settings, then asks a detached
/// shell to delete the install folder once this process has exited.
/// </summary>
public static class Uninstaller
{
    public const string AppName = "Cymple Bridge";
    public const string Version = "1.0.0";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CympleBridge";

    public static string InstallDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    public static string DesktopShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk");

    public static string StartMenuShortcut =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Start Menu\Programs", $"{AppName}.lnk");

    public static string SettingsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CympleBridge");

    public static void Run()
    {
        TryDeleteFile(DesktopShortcut);
        TryDeleteFile(StartMenuShortcut);
        TryDeleteTree(UninstallKey);
        TryDeleteDir(SettingsDir);
        ScheduleFolderDeletion(InstallDir);
    }

    public static void Register(string exePath, string installDir, long sizeInKb)
    {
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", AppName);
        key.SetValue("InstallLocation", installDir);
        key.SetValue("DisplayIcon", exePath);
        key.SetValue("UninstallString", $"\"{exePath}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{exePath}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)Math.Min(sizeInKb, int.MaxValue), RegistryValueKind.DWord);
    }

    private static void ScheduleFolderDeletion(string dir)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c ping 127.0.0.1 -n 2 >nul & rmdir /s /q \"{dir}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
        catch { /* best effort */ }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
    }

    private static void TryDeleteTree(string subKey)
    {
        try { Registry.CurrentUser.DeleteSubKeyTree(subKey, false); } catch { }
    }

    public static bool IsInstalled()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(UninstallKey); return k is not null; }
        catch { return false; }
    }
}

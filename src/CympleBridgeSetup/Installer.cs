using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace CympleBridgeSetup;

public static class Installer
{
    public const string AppName = "Cymple Bridge";
    public const string Version = "1.0.0";
    public const string ExeName = "CympleBridge.exe";
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\CympleBridge";

    public static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CympleBridge");

    public static string DesktopShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{AppName}.lnk");

    public static string StartMenuShortcut => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Windows\Start Menu\Programs", $"{AppName}.lnk");

    public static bool IsInstalled()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(UninstallKey); return k is not null; }
        catch { return false; }
    }

    public static string? InstalledExe
    {
        get
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(UninstallKey);
                return k?.GetValue("DisplayIcon") as string;
            }
            catch { return null; }
        }
    }

    public static void Install(string dir, bool launch, Action<double, string>? progress = null)
    {
        progress?.Invoke(0, "Stopping running instance");
        KillRunning();

        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, ExeName);
        var tmp = exe + ".new";

        progress?.Invoke(2, "Extracting application");
        using (var resource = OpenPayload())
        using (var output = File.Create(tmp))
        {
            bool compressed = IsGzip(resource);
            long total = resource.Length;
            long done = 0;
            using var source = compressed ? new GZipStream(resource, CompressionMode.Decompress) : resource;
            var buffer = new byte[1 << 20];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                done += read;
                double fraction = compressed ? (double)resource.Position / total : (double)done / total;
                progress?.Invoke(2 + 88.0 * fraction, "Extracting application");
            }
        }

        if (File.Exists(exe)) File.Delete(exe);
        File.Move(tmp, exe);

        progress?.Invoke(92, "Creating shortcuts");
        CreateShortcut(DesktopShortcut, exe, dir);
        CreateShortcut(StartMenuShortcut, exe, dir);

        progress?.Invoke(97, "Registering uninstaller");
        Register(exe, dir);

        progress?.Invoke(100, "Installed");
        if (launch)
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = dir, UseShellExecute = true });
    }

    private static Stream OpenPayload()
    {
        var asm = Assembly.GetExecutingAssembly();
        var names = asm.GetManifestResourceNames();
        var name = names.FirstOrDefault(n => n.EndsWith(ExeName + ".gz", StringComparison.OrdinalIgnoreCase))
                   ?? names.FirstOrDefault(n => n.EndsWith(ExeName, StringComparison.OrdinalIgnoreCase))
                   ?? throw new InvalidOperationException(
                       $"No embedded '{ExeName}' payload. Run scripts/build-release.ps1 first.");
        return asm.GetManifestResourceStream(name)!;
    }

    private static bool IsGzip(Stream stream)
    {
        if (!stream.CanSeek || stream.Length < 2)
            return false;
        int b0 = stream.ReadByte();
        int b1 = stream.ReadByte();
        stream.Position = 0;
        return b0 == 0x1F && b1 == 0x8B;
    }

    private static void Register(string exe, string dir)
    {
        long sizeKb = new FileInfo(exe).Length / 1024;
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", AppName);
        key.SetValue("InstallLocation", dir);
        key.SetValue("DisplayIcon", exe);
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", (int)Math.Min(sizeKb, int.MaxValue), RegistryValueKind.DWord);
    }

    private static void CreateShortcut(string path, string exe, string workingDir)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell");
        if (shellType is null) return;

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = exe;
        shortcut.WorkingDirectory = workingDir;
        shortcut.IconLocation = exe + ",0";
        shortcut.Description = "Cymple-Babble mouth tracker relay";
        shortcut.Save();
    }

    private static void KillRunning()
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(ExeName)))
        {
            try
            {
                p.CloseMainWindow();
                if (!p.WaitForExit(2500))
                    p.Kill();
            }
            catch { }
        }
    }
}

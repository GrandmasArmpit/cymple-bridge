using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using Size = System.Windows.Size;

namespace CympleBridgeSetup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Headless render used to eyeball the installer design.
        int sidx = Array.IndexOf(e.Args, "--screenshot");
        if (sidx >= 0 && sidx + 1 < e.Args.Length)
        {
            var path = e.Args[sidx + 1];
            var win = new MainWindow();
            win.Show();
            win.Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(900);
                Render(win, path);
                win.Close();
                Shutdown();
            }, System.Windows.Threading.DispatcherPriority.Background);
            return;
        }

        // Headless install used by the packaging test: --silent [--dir PATH] [--nolaunch]
        if (e.Args.Contains("--silent"))
        {
            var dir = Arg(e.Args, "--dir") ?? Installer.DefaultDir;
            var launch = !e.Args.Contains("--nolaunch");
            try
            {
                Installer.Install(dir, launch);
                Shutdown(0);
            }
            catch
            {
                Shutdown(1);
            }
            return;
        }

        new MainWindow().Show();
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void Render(Window window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var bmp = new RenderTargetBitmap(
            (int)(window.ActualWidth * dpi.DpiScaleX),
            (int)(window.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}

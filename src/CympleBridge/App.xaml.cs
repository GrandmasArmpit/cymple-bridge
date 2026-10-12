using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Application = System.Windows.Application;
using Size = System.Windows.Size;

namespace CympleBridge;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Registered as the Add/Remove Programs uninstall command.
        if (e.Args.Contains("--uninstall"))
        {
            CympleBridge.Bridge.Uninstaller.Run();
            Shutdown();
            return;
        }

        // Headless relay used by the end-to-end test harness: --run <url> [--seconds N]
        int ridx = Array.IndexOf(e.Args, "--run");
        if (ridx >= 0 && ridx + 1 < e.Args.Length)
        {
            var url = e.Args[ridx + 1];
            int seconds = 4;
            int sidx = Array.IndexOf(e.Args, "--seconds");
            if (sidx >= 0 && sidx + 1 < e.Args.Length) seconds = int.Parse(e.Args[sidx + 1]);

            var runner = new CympleBridge.Bridge.BridgeRunner();
            long frames = 0;
            runner.Stats += s => Interlocked.Exchange(ref frames, s.Frames);
            runner.Start(url);

            Task.Run(async () =>
            {
                await Task.Delay(seconds * 1000);
                runner.Stop();
                runner.Dispose();
                Dispatcher.Invoke(() => Shutdown(Interlocked.Read(ref frames) > 0 ? 0 : 1));
            });
            return;
        }

        var window = new MainWindow();

        // Headless render used during development to eyeball the design.
        int idx = Array.IndexOf(e.Args, "--screenshot");
        if (idx >= 0 && idx + 1 < e.Args.Length)
        {
            var path = e.Args[idx + 1];
            if (e.Args.Contains("--demo"))
                window.ApplyDemo();
            window.Show();
            window.Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(1600);
                Render(window, path);
                window.Close();
                Shutdown();
            }, System.Windows.Threading.DispatcherPriority.Background);
            return;
        }

        window.Show();
    }

    private static void Render(Window window, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var size = new Size(window.ActualWidth, window.ActualHeight);
        var bmp = new RenderTargetBitmap(
            (int)(size.Width * dpi.DpiScaleX),
            (int)(size.Height * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var fs = File.Create(path);
        encoder.Save(fs);
    }
}

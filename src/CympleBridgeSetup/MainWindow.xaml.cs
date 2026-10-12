using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace CympleBridgeSetup;

public partial class MainWindow : Window
{
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        PathBox.Text = DisplayPath;
        RefreshState();
    }

    private void RefreshState()
    {
        var installed = Installer.IsInstalled();
        StateWord.Text = installed ? "INSTALLED" : "NOT INSTALLED";
        SetLed(installed ? "#FF5AF2A6" : "#FF8494A0");

        InstallButton.Content = installed ? "REINSTALL" : "INSTALL";
        UninstallButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;

        InstallButton.IsEnabled = !_busy;
        UninstallButton.IsEnabled = !_busy;
    }

    private void SetLed(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        StateLed.Fill = new SolidColorBrush(color);
        StateWord.Foreground = new SolidColorBrush(color);
        if (StateLed.Effect is DropShadowEffect glow)
            glow.Color = color;
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        BusyPanel.Visibility = Visibility.Visible;
        InstallButton.IsEnabled = false;
        UninstallButton.IsEnabled = false;
        SetLed("#FFFFB454");
        StateWord.Text = "INSTALLING";

        var launch = LaunchCheck.IsChecked == true;
        var dir = Installer.DefaultDir;
        PathBox.Text = DisplayPath;

        void Report(double pct, string message) => Dispatcher.Invoke(() =>
        {
            Progress.Value = pct;
            StatusText.Text = message;
        });

        var error = await Task.Run(() =>
        {
            try { Installer.Install(dir, launch, Report); return null; }
            catch (Exception ex) { return ex.Message; }
        });

        _busy = false;
        if (error is null)
        {
            Progress.Value = 100;
            StatusText.Text = "Installed";
            SetLed("#FF5AF2A6");
            StateWord.Text = "INSTALLED";
            RefreshState();
        }
        else
        {
            StatusText.Text = error;
            SetLed("#FFFF6B6B");
            StateWord.Text = "FAILED";
            InstallButton.IsEnabled = true;
        }
    }

    private void OnUninstall(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var exe = Installer.InstalledExe;
        if (exe is null || !System.IO.File.Exists(exe))
        {
            RefreshState();
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe, "--uninstall") { UseShellExecute = true });
        }
        catch { }

        // The app removes the registry entry first, so re-check shortly after.
        Dispatcher.InvokeAsync(async () =>
        {
            for (int i = 0; i < 20 && Installer.IsInstalled(); i++)
                await Task.Delay(150);
            RefreshState();
        });
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    // Show the environment-variable form; the expanded path leaks the user name.
    private const string DisplayPath = @"%LOCALAPPDATA%\Programs\CympleBridge";
}

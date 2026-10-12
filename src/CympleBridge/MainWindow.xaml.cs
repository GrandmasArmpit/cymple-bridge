using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using CympleBridge.Bridge;
using WinForms = System.Windows.Forms;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace CympleBridge;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AppSettings _settings;
    private readonly BridgeRunner _runner = new();

    public ObservableCollection<LogEntry> Logs { get; } = new();

    public MainWindow()
    {
        _settings = AppSettings.Load();
        _url = _settings.Url;

        InitializeComponent();
        DataContext = this;
        RestoreWindowBounds();

        _runner.Log += (level, message) => Dispatcher.Invoke(() => AddLog(level, message));
        _runner.Stats += stats => Dispatcher.Invoke(() => ApplyStats(stats));
        _runner.StateChanged += (state, text) => Dispatcher.Invoke(() => ApplyState(state, text));

        Logs.CollectionChanged += (_, _) =>
            Dispatcher.InvokeAsync(() => ConsoleScroll.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);

        AddLog("info", "Cymple Bridge ready");
        AddLog("info", "Waiting for relay");
    }

    // ---- bindable state ----

    private string _url;
    public string Url
    {
        get => _url;
        set
        {
            if (_url == value) return;
            _url = value;
            OnPropertyChanged(nameof(Url));
            _settings.Url = value;
            _settings.Save();
        }
    }

    private bool _isOn;
    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (_isOn == value) return;
            _isOn = value;
            OnPropertyChanged(nameof(IsOn));

            if (value)
            {
                if (string.IsNullOrWhiteSpace(Url) || !Uri.TryCreate(Url, UriKind.Absolute, out _))
                {
                    AddLog("err", "Enter a valid stream URL before starting");
                    _isOn = false;
                    OnPropertyChanged(nameof(IsOn));
                    return;
                }
                _runner.Start(Url.Trim());
                AddLog("info", "Relay engaged");
            }
            else
            {
                _runner.Stop();
                AddLog("info", "Relay disengaged");
            }
        }
    }

    private string _stateText = "IDLE";
    public string StateText
    {
        get => _stateText;
        private set { _stateText = value; OnPropertyChanged(nameof(StateText)); }
    }

    private Brush _stateBrush = new SolidColorBrush(Color.FromRgb(0x84, 0x94, 0xA0));
    public Brush StateBrush
    {
        get => _stateBrush;
        private set { _stateBrush = value; OnPropertyChanged(nameof(StateBrush)); }
    }

    private double _fps;
    public double Fps
    {
        get => _fps;
        private set { _fps = value; OnPropertyChanged(nameof(Fps)); }
    }

    private string _fpsText = "0.0";
    public string FpsText { get => _fpsText; private set { _fpsText = value; OnPropertyChanged(nameof(FpsText)); } }

    private string _framesText = "0";
    public string FramesText { get => _framesText; private set { _framesText = value; OnPropertyChanged(nameof(FramesText)); } }

    private string _packetsText = "0";
    public string PacketsText { get => _packetsText; private set { _packetsText = value; OnPropertyChanged(nameof(PacketsText)); } }

    private string _uptimeText = "00:00:00";
    public string UptimeText { get => _uptimeText; private set { _uptimeText = value; OnPropertyChanged(nameof(UptimeText)); } }

    // ---- event handlers ----

    private void ApplyStats(BridgeStats s)
    {
        Fps = Math.Min(s.Fps, 60);
        FpsText = s.Fps.ToString("0.0");
        FramesText = s.Frames.ToString("N0");
        PacketsText = s.Packets.ToString("N0");
        UptimeText = s.Uptime.ToString(@"hh\:mm\:ss");
    }

    private void ApplyState(BridgeState state, string text)
    {
        StateText = text;
        (StateBrush, var color) = state switch
        {
            BridgeState.Live => (new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5AF2A6")), (Color)ColorConverter.ConvertFromString("#FF5AF2A6")),
            BridgeState.Linking => (new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFB454")), (Color)ColorConverter.ConvertFromString("#FFFFB454")),
            BridgeState.Error => (new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFF6B6B")), (Color)ColorConverter.ConvertFromString("#FFFF6B6B")),
            _ => (new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8494A0")), (Color)ColorConverter.ConvertFromString("#FF8494A0")),
        };

        if (Led.Effect is DropShadowEffect glow)
            glow.Color = color;
    }

    private void AddLog(string level, string message)
    {
        var brush = level switch
        {
            "ok" => (Brush)FindResource("B.Green"),
            "warn" => (Brush)FindResource("B.Amber"),
            "err" => (Brush)FindResource("B.Red"),
            _ => (Brush)FindResource("B.TextFaint"),
        };

        Logs.Add(new LogEntry(DateTime.Now.ToString("HH:mm:ss"), level.ToUpperInvariant(), message, brush));
        if (Logs.Count > 500)
            Logs.RemoveAt(0);
    }

    /// <summary>Populates the live look for design screenshots. No network activity.</summary>
    public void ApplyDemo()
    {
        _url = "http://192.168.1.50:8000/stream.mjpg";
        OnPropertyChanged(nameof(Url));
        ApplyState(BridgeState.Live, "LIVE");
        ApplyStats(new BridgeStats(43.5, 18342, 41230, new TimeSpan(0, 7, 12)));
        AddLog("info", "Opening stream  http://192.168.1.50:8000/stream.mjpg");
        AddLog("ok", "Link established (200 OK)");
        AddLog("info", "First frame relayed · flag 2 (mouth)");
        AddLog("warn", "Frame drop recovered, stream resumed");
        _isOn = true;
        OnPropertyChanged(nameof(IsOn));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // staggered panel reveal
        var children = ContentRoot.Children.Cast<UIElement>().OfType<FrameworkElement>().ToList();
        for (int i = 0; i < children.Count; i++)
        {
            var el = children[i];
            var begin = TimeSpan.FromMilliseconds(40 + i * 75);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            el.Opacity = 0;
            var slide = new TranslateTransform(0, 16);
            el.RenderTransform = slide;

            el.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(340)) { BeginTime = begin, EasingFunction = ease });
            slide.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(460)) { BeginTime = begin, EasingFunction = ease });
        }
    }

    private void OnClearLogs(object sender, RoutedEventArgs e) => Logs.Clear();

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void RestoreWindowBounds()
    {
        if (_settings.WindowWidth is > 700 and < 6000) Width = _settings.WindowWidth.Value;
        if (_settings.WindowHeight is > 500 and < 4000) Height = _settings.WindowHeight.Value;
        if (_settings.WindowLeft is not null && _settings.WindowTop is not null)
        {
            Left = _settings.WindowLeft.Value;
            Top = _settings.WindowTop.Value;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
    }

    // ---- system tray ----

    private WinForms.NotifyIcon? _tray;
    private bool _exitRequested;

    private void OnClose(object sender, RoutedEventArgs e) => HideToTray();

    private void EnsureTray()
    {
        if (_tray is not null)
            return;

        System.Drawing.Icon icon;
        try { icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application; }
        catch { icon = System.Drawing.SystemIcons.Application; }

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open Cymple Bridge", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Close Cymple Bridge", null, (_, _) => ExitApplication());

        _tray = new WinForms.NotifyIcon
        {
            Icon = icon,
            Text = "Cymple Bridge",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void HideToTray()
    {
        EnsureTray();
        Hide();
        _tray!.ShowBalloonTip(2500, "Cymple Bridge",
            "Still running — the relay stays active in the tray.", WinForms.ToolTipIcon.Info);
    }

    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        Close();
    }

    private void PersistSettings()
    {
        _settings.Url = Url;
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
        _settings.WindowLeft = bounds.Left;
        _settings.WindowTop = bounds.Top;
        _settings.Save();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exitRequested)
        {
            // The window close button parks the app in the tray; only the tray menu exits.
            e.Cancel = true;
            HideToTray();
            return;
        }

        PersistSettings();
        _runner.Stop();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray?.Dispose();
        _tray = null;
        _runner.Dispose();
        base.OnClosed(e);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record LogEntry(string Time, string Level, string Message, Brush LevelBrush);

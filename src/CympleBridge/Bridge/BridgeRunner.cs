using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace CympleBridge.Bridge;

public enum BridgeState { Idle, Linking, Live, Error }

public sealed record BridgeStats(double Fps, long Frames, long Packets, TimeSpan Uptime);

/// <summary>
/// Reads a wireless tracker's MJPEG stream and relays it to the Cymple desktop app as a
/// mouth device. Runs on a background task; raises events for the UI.
/// </summary>
public sealed class BridgeRunner : IDisposable
{
    public event Action<string, string>? Log;          // level, message
    public event Action<BridgeStats>? Stats;
    public event Action<BridgeState, string>? StateChanged;

    private readonly UdpClient _udp = new();
    private readonly IPEndPoint _target = new(IPAddress.Loopback, 23303);
    private readonly object _sync = new();

    private CancellationTokenSource? _cts;
    private Task? _task;

    public bool IsRunning { get { lock (_sync) return _task is { IsCompleted: false }; } }

    public void Start(string url)
    {
        lock (_sync)
        {
            if (_task is { IsCompleted: false })
                return;
            _cts = new CancellationTokenSource();
            _task = Task.Run(() => RunAsync(url, _cts.Token));
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _cts?.Cancel();
        }
    }

    private async Task RunAsync(string url, CancellationToken token)
    {
        StateChanged?.Invoke(BridgeState.Linking, "LINKING");
        Log?.Invoke("info", $"Opening stream  {url}");

        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var totalFrames = 0L;
        var totalPackets = 0L;
        var sw = Stopwatch.StartNew();
        var fpsWindow = Stopwatch.StartNew();
        var framesInWindow = 0;
        double fps = 0;
        byte frameIndex = 0;
        var live = false;

        while (!token.IsCancellationRequested)
        {
            try
            {
                using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
                resp.EnsureSuccessStatusCode();

                if (!live)
                {
                    live = true;
                    StateChanged?.Invoke(BridgeState.Live, "LIVE");
                    Log?.Invoke("ok", $"Link established ({(int)resp.StatusCode} {resp.ReasonPhrase})");
                }

                await using var stream = await resp.Content.ReadAsStreamAsync(token);
                var framer = new JpegFramer();
                var buf = new byte[64 * 1024];

                while (!token.IsCancellationRequested)
                {
                    int n = await stream.ReadAsync(buf, token);
                    if (n <= 0)
                        throw new IOException("stream closed");

                    foreach (var jpeg in framer.Push(buf, n))
                    {
                        var encoded = Reencode(jpeg);
                        if (encoded is null)
                            continue;

                        foreach (var packet in CymplePackets.BuildSegments(encoded, CymplePackets.FlagMouth, frameIndex))
                        {
                            _udp.Send(packet, packet.Length, _target);
                            totalPackets++;
                        }

                        frameIndex++;
                        totalFrames++;
                        framesInWindow++;

                        if (fpsWindow.ElapsedMilliseconds >= 500)
                        {
                            fps = framesInWindow / fpsWindow.Elapsed.TotalSeconds;
                            framesInWindow = 0;
                            fpsWindow.Restart();
                            Stats?.Invoke(new BridgeStats(fps, totalFrames, totalPackets, sw.Elapsed));
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                if (live)
                {
                    live = false;
                    StateChanged?.Invoke(BridgeState.Error, "RETRYING");
                }
                Log?.Invoke("warn", $"Stream error: {e.Message}");
                try { await Task.Delay(1500, token); }
                catch (OperationCanceledException) { break; }
            }
        }

        StateChanged?.Invoke(BridgeState.Idle, "IDLE");
        Log?.Invoke("info", "Relay stopped");
    }

    // Decode, fit to 320x240, re-encode as JPEG. The Cymple mouth model works on that frame size.
    private static byte[]? Reencode(byte[] jpeg)
    {
        try
        {
            using var input = new MemoryStream(jpeg);
            using var image = Image.FromStream(input);

            using var bmp = new Bitmap(320, 240, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(image, 0, 0, 320, 240);
            }

            using var output = new MemoryStream();
            var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, 70L);
            bmp.Save(output, encoder, parameters);
            return output.ToArray();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udp.Dispose();
        _cts?.Dispose();
    }
}

/// <summary>Small JSON settings store under %APPDATA%\CympleBridge.</summary>
public sealed class AppSettings
{
    public string Url { get; set; } = "http://192.168.1.20:8000/stream.mjpg";
    public double? WindowWidth { get; set; }
    public double? WindowHeight { get; set; }
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CympleBridge");

    private static string File => Path.Combine(Dir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (System.IO.File.Exists(File))
                return JsonSerializer.Deserialize<AppSettings>(System.IO.File.ReadAllText(File)) ?? new AppSettings();
        }
        catch { /* fall through to defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* settings are best-effort */ }
    }
}

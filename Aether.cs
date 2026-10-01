using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace AetherGui;

record Preset(string Name, string Args, int StartupSecs = Aether.FastSecs, string HttpFlag = "--http-proxy")
{
    public bool ManualOnly => StartupSecs > Aether.FastSecs; // too slow to wait for in the Auto rotation
    public string Label => ManualOnly ? Name + " (manual)" : Name;
}

// Everything about aether.exe itself: where it lives, downloading it, starting it, and testing its tunnel.
static class Aether
{
    private const string TestUrl = "http://www.gstatic.com/generate_204";
    private const int ConnectTimeoutMs = 10_000; // after the proxy is up, the internet test must pass within this
    public const int HttpPort = 1819;            // HTTP proxy, set as the system proxy
    private const int SocksPort = 1818;          // SOCKS5, used for the delay test (free for manual use too)
    private static readonly string Dir = AppContext.BaseDirectory; // aether.exe lives beside this exe
    private static readonly string Exe = Path.Combine(Dir, "aether.exe");

    private const string Warp = " --turbo -4 --quick-reconnect";
    public const int FastSecs = 30; // startup budget of the fast protocols, the only ones Auto rotates through

    // The protocol menu aether itself offers. Tor/Psiphon bootstrap slowly (tor tries 75s plainly, then bridges;
    // psiphon waits up to 180s), so they get longer startup budgets and run only when picked by hand.
    // The "alone" modes don't serve --http-proxy, so they expose their own HTTP proxy flag on the same port.
    public static readonly Preset[] Presets =
    {
        new("MASQUE", "--masque" + Warp),
        new("WireGuard", "--wg" + Warp),
        new("WARP-in-WARP (gool)", "--gool" + Warp),
        new("MASQUE-in-MASQUE", "--mim" + Warp),
        new("Tor alone", "--tor-only", 300, "--tor-http"),
        new("Tor + WARP", "--tor-reverse" + Warp, 300),         // warp reached via tor, exit via warp
        new("Psiphon alone", "--psiphon-only", 240, "--psiphon-http"),
        new("Psiphon + WARP", "--psiphon-reverse" + Warp, 240), // warp reached via psiphon, exit via warp
    };

    public static readonly Preset[] AutoPresets = Presets.Where(p => !p.ManualOnly).ToArray();

    // A real request through aether's SOCKS5 proxy measures the real delay.
    private static readonly HttpClient Http = new(new SocketsHttpHandler { Proxy = new WebProxy($"socks5://127.0.0.1:{SocksPort}") })
    { Timeout = TimeSpan.FromSeconds(3) };

    // If aether.exe is missing, download the latest Windows release from GitHub and verify its SHA-256.
    // The whole archive is kept: pt/ holds the tor bridge transports and psiphon, which aether looks for beside itself.
    public static async Task<bool> EnsureInstalledAsync(Action<string> log, CancellationToken ct)
    {
        if (File.Exists(Exe)) return true;
        try
        {
            log("aether.exe not found, downloading the latest release from GitHub...");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AetherGui");
            using var release = System.Text.Json.JsonDocument.Parse(
                await http.GetStringAsync("https://api.github.com/repos/CluvexStudio/Aether/releases/latest", ct));
            string? Asset(string name) => release.RootElement.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a => a.GetProperty("name").GetString() == name)
                .TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;

            var zipUrl = Asset("aether-windows-x86_64.zip") ?? throw new Exception("no Windows asset in the latest release");
            var zip = await http.GetByteArrayAsync(zipUrl, ct);
            if (Asset("aether-windows-x86_64.zip.sha256") is { } shaUrl)
            {
                var expected = (await http.GetStringAsync(shaUrl, ct)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)[0];
                if (!Convert.ToHexString(SHA256.HashData(zip)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new Exception("SHA-256 mismatch, download discarded");
            }
            using var archive = new ZipArchive(new MemoryStream(zip));
            if (archive.GetEntry("aether.exe") is null) throw new Exception("aether.exe not in the archive");
            archive.ExtractToDirectory(Dir, true);
            log($"Downloaded {release.RootElement.GetProperty("tag_name").GetString()}.");
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log($"Download failed: {ex.Message}");
            return false;
        }
    }

    // SOCKS5 (aether default) for our own test, HTTP proxy for the system proxy.
    public static Process? Start(Preset p, Action<string> log)
    {
        try
        {
            var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = Exe,
                    Arguments = p.Args + $" --bind 127.0.0.1:{SocksPort} {p.HttpFlag} 127.0.0.1:{HttpPort}",
                    WorkingDirectory = Dir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) log(e.Data); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            return proc;
        }
        catch (Exception ex) { log($"Start failed: {ex.Message}"); return null; }
    }

    // Waits for a passing internet test through aether's proxy. Gives up once the startup budget is spent
    // and the proxy has been up for ConnectTimeoutMs without passing (tor opens its port long before it can carry traffic).
    public static async Task<int?> WaitConnectedAsync(Process proc, int startupSecs, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long? upAt = null;
        while (!proc.HasExited)
        {
            if (upAt is null && await PortOpenAsync(ct)) upAt = sw.ElapsedMilliseconds;
            if (upAt is not null && await PingAsync(ct) is int ms) return ms;
            var t = sw.ElapsedMilliseconds;
            if (t > startupSecs * 1000 && (upAt is null || t - upAt > ConnectTimeoutMs)) return null;
            await Task.Delay(500, ct);
        }
        return null;
    }

    private static async Task<bool> PortOpenAsync(CancellationToken ct)
    {
        try { using var c = new TcpClient(); await c.ConnectAsync("127.0.0.1", SocksPort, ct); return true; }
        catch (Exception) when (!ct.IsCancellationRequested) { return false; }
    }

    public static async Task<int?> PingAsync(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, TestUrl);
            req.Headers.ConnectionClose = true;
            using var res = await Http.SendAsync(req, ct);
            return res.StatusCode == HttpStatusCode.NoContent ? (int)sw.ElapsedMilliseconds : null;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { return null; }
    }
}

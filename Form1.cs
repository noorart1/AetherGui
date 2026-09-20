using System.Diagnostics;
using Microsoft.Win32;

namespace AetherGui;

public partial class Form1 : Form
{
    private const int StartupDelayMs = 3000; // pause before the automatic connect on startup
    private const string SettingsKey = @"Software\AetherGui";

    private Process? _process;
    private CancellationTokenSource? _cts;

    public Form1()
    {
        InitializeComponent();
        using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
        {
            chkStartup.Checked = key?.GetValue("AutoConnect") is 1;
            chkSysProxy.Checked = key?.GetValue("SetSystemProxy") is 1;
        }
        chkStartup.CheckedChanged += (_, _) => SaveSetting("AutoConnect", chkStartup.Checked);
        chkSysProxy.CheckedChanged += (_, _) =>
        {
            SaveSetting("SetSystemProxy", chkSysProxy.Checked);
            if (!chkSysProxy.Checked) SystemProxy.Disable();
            else if (_process is { HasExited: false } && lblStatus.Text.Contains("Connected:")) SystemProxy.Enable();
        };
        Shown += async (_, _) =>
        {
            if (!chkStartup.Checked) return;
            Log($"Auto-connecting in {StartupDelayMs / 1000}s...");
            await Task.Delay(StartupDelayMs);
            if (chkStartup.Checked && _cts == null) btnConnect_Click(null, EventArgs.Empty);
        };
        FormClosing += (_, _) => { _cts?.Cancel(); KillProcess(); SystemProxy.Disable(); };
    }

    private static void SaveSetting(string name, bool value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
        key.SetValue(name, value ? 1 : 0);
    }

    private async void btnConnect_Click(object? sender, EventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        _cts = new CancellationTokenSource();
        StartWatchdog();
        btnConnect.Text = "Disconnect";
        await RunAsync(_cts.Token);
        _cts = null;
        btnConnect.Text = "Auto Connect";
    }

    // Try presets in random order; a preset that times out or drops is not retried until all others were tried.
    private async Task RunAsync(CancellationToken ct)
    {
        var untried = new List<Preset>();
        try
        {
            SetStatus("Downloading aether...", Color.Orange);
            if (!await Aether.EnsureInstalledAsync(Log, ct)) return;
            while (true)
            {
                if (untried.Count == 0) untried.AddRange(Aether.Presets);
                var p = untried[Random.Shared.Next(untried.Count)];
                untried.Remove(p);
                await ConnectAndMonitorAsync(p, ct);
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            KillProcess();
            SetStatus("Disconnected", Color.Red);
            lblDelay.Text = "Delay: -";
        }
    }

    // Returns when the preset fails to connect within the timeout, or after a connected session drops.
    private async Task ConnectAndMonitorAsync(Preset p, CancellationToken ct)
    {
        Log($"Trying {p.Name}...");
        SetStatus($"Connecting: {p.Name}", Color.Orange);
        var proc = _process = Aether.Start(p, Log);
        if (proc is null) return;

        var ms = await Aether.WaitConnectedAsync(proc, ct);
        if (ms is null)
        {
            Log(proc.HasExited ? $"{p.Name}: process exited." : $"{p.Name}: no internet, switching.");
            KillProcess();
            return;
        }

        if (chkSysProxy.Checked) SystemProxy.Enable();
        SetStatus($"Connected: {p.Name}", Color.SeaGreen);
        Log($"Connected via {p.Name}, delay {ms} ms.");
        for (int fails = 0; fails < 2;)
        {
            lblDelay.Text = ms is null ? "Delay: timeout" : $"Delay: {ms} ms";
            await Task.Delay(5000, ct);
            if (proc.HasExited) break;
            ms = await Aether.PingAsync(ct);
            fails = ms is null ? fails + 1 : 0;
        }
        Log($"{p.Name}: connection lost, switching.");
        KillProcess();
    }

    private bool _watchdogStarted;

    // If this GUI crashes or is killed, the watchdogs stop aether and clear the system proxy (two independent layers).
    private void StartWatchdog()
    {
        if (_watchdogStarted) return;
        _watchdogStarted = true;
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, $"--watchdog {Environment.ProcessId}") { CreateNoWindow = true, UseShellExecute = false });
        SystemProxy.StartIndependentGuard(Environment.ProcessId);
    }

    private void KillProcess()
    {
        SystemProxy.Disable(); // never leave the system pointing at a dead proxy
        try { if (_process is { HasExited: false }) { _process.Kill(true); _process.WaitForExit(2000); } } catch { }
        _process?.Dispose();
        _process = null;
    }

    private void ShowProxyInfo()
    {
        var secrets = Directory.GetFiles(AppContext.BaseDirectory, "aether*.toml").Length;
        var head = Color.Cyan; var info = Color.White; var tip = Color.Gold;
        Log("=== Proxy info ===", head);
        Log($"System proxy: {SystemProxy.Status()}", info);
        Log($"HTTP {Aether.HttpPort} (system proxy) | SOCKS5 1818 (delay test, free for manual use)", info);
        Log($"aether.exe: {(_process is { HasExited: false } ? "running" : "stopped")} | {lblStatus.Text.TrimStart('●', ' ')} | {lblDelay.Text}", info);
        Log("Presets: " + string.Join(", ", Aether.Presets.Select(p => p.Name)), info);
        Log("Cleanup: form close, --watchdog copy, hidden PowerShell guard, reset on startup.", info);
        Log("=== Recommendations ===", head);
        Log($"1. {secrets} aether*.toml file(s) beside the exe hold private keys/tokens: keep this folder private, rotate keys if shared.", tip);
        Log("2. Enable 'Set system proxy' only when needed; anything with its own proxy settings can bypass it.", tip);
        Log("3. If Delay shows timeout repeatedly, the app auto-switches presets; try Masque (HTTP/2) on restrictive networks.", tip);
    }

    private void SetStatus(string text, Color color)
    {
        lblStatus.Text = "\u25CF " + text;
        lblStatus.ForeColor = color;
    }

    private void Log(string msg) => Log(msg, null);

    private void Log(string msg, Color? color)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(msg, color)); return; }
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.SelectionColor = color ?? txtLog.ForeColor;
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        txtLog.SelectionColor = txtLog.ForeColor;
        txtLog.ScrollToCaret();
    }
}

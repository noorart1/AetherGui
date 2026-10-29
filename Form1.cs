using System.Diagnostics;
using Microsoft.Win32;

namespace AetherGui;

public partial class Form1 : Form
{
    private const int StartupDelayMs = 3000; // pause before the automatic connect on startup
    private const string SettingsKey = @"Software\AetherGui";

    private Process? _process;
    private CancellationTokenSource? _cts;
    private CoreOptions _coreOptions = new();
    private readonly int[] _protocolIndexes = [-1, .. Aether.MenuPresetIndexes];
    private static readonly string[] ScanModes = ["turbo", "balanced"]; // saved as an index into this
    private Dictionary<int, int> _connectMs = []; // preset index -> ms its last connect took, -1 = failed; orders Auto

    public Form1()
    {
        InitializeComponent();
        cboProtocol.Items.Add("Auto (order in Core settings)");
        cboProtocol.Items.AddRange(Aether.MenuPresetIndexes.Select(i => (object)Aether.Presets[i].Label).ToArray());
        cboScan.Items.AddRange(["Fast: first server that answers", "Balanced: tries a few, keeps the fastest"]);
        using (var key = Registry.CurrentUser.OpenSubKey(SettingsKey))
        {
            chkStartup.Checked = key?.GetValue("AutoConnect") is 1;
            chkSysProxy.Checked = key?.GetValue("SetSystemProxy") is 1;
            chkPsiphon.Checked = key?.GetValue("RunPsiphon") is not 0; // on unless turned off, as before this option
            var saved = key?.GetValue("Protocol") is int i && i >= 0 && i <= Aether.Presets.Length ? i : 0;
            cboProtocol.SelectedIndex = Array.IndexOf(_protocolIndexes, saved - 1);
            cboScan.SelectedIndex = key?.GetValue("Scan") is 1 ? 1 : 0;
            _connectMs = Aether.ParseConnectMs(key?.GetValue("ConnectMs") as string);
        }
        try { _coreOptions = CoreOptions.Load(); }
        catch { chkStartup.Checked = false; Log("Could not load core settings. Review settings before connecting."); }
        cboProtocol.SelectedIndexChanged += (_, _) => SaveSetting("Protocol", _protocolIndexes[cboProtocol.SelectedIndex] + 1);
        cboScan.SelectedIndexChanged += (_, _) => SaveSetting("Scan", cboScan.SelectedIndex);
        chkStartup.CheckedChanged += (_, _) => SaveSetting("AutoConnect", chkStartup.Checked);
        chkSysProxy.CheckedChanged += (_, _) =>
        {
            SaveSetting("SetSystemProxy", chkSysProxy.Checked);
            if (!chkSysProxy.Checked) SystemProxy.Disable();
            else if (_process is { HasExited: false } && lblStatus.Text.Contains("Connected:")) SystemProxy.Enable();
        };
        chkPsiphon.CheckedChanged += (_, _) =>
        {
            SaveSetting("RunPsiphon", chkPsiphon.Checked);
            if (chkPsiphon.Checked) return;
            Psiphon.Kill();
            SystemProxy.Disable(); // the proxy Psiphon set is dead now
            if (chkSysProxy.Checked && _process is { HasExited: false } && lblStatus.Text.Contains("Connected:")) SystemProxy.Enable();
        };
        Shown += async (_, _) =>
        {
            _ = CheckUpdatesAsync(interactive: false);
            if (!chkStartup.Checked) return;
            Log($"Auto-connecting in {StartupDelayMs / 1000}s...");
            await Task.Delay(StartupDelayMs);
            if (chkStartup.Checked && _cts == null) btnConnect_Click(null, EventArgs.Empty);
        };
        FormClosing += (_, _) => { _cts?.Cancel(); KillProcess(); Psiphon.Kill(); SystemProxy.Disable(); };
    }

    private static void SaveSetting(string name, bool value) => SaveSetting(name, value ? 1 : 0);

    private static void SaveSetting(string name, object value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(SettingsKey);
        key.SetValue(name, value);
    }

    private async void btnConnect_Click(object? sender, EventArgs e)
    {
        if (_cts != null) { _cts.Cancel(); return; }
        _cts = new CancellationTokenSource();
        StartWatchdog();
        btnConnect.Text = "Disconnect";
        btnConnect.AccessibleName = "Disconnect";
        btnCoreSettings.Enabled = cboProtocol.Enabled = cboScan.Enabled = btnUpdates.Enabled = false;
        btnCoreInput.Enabled = true;
        await RunAsync(_cts.Token);
        _cts.Dispose();
        _cts = null;
        if (IsDisposed) return;
        btnCoreSettings.Enabled = cboProtocol.Enabled = cboScan.Enabled = btnUpdates.Enabled = true;
        btnCoreInput.Enabled = false;
        btnConnect.Text = "Connect securely";
        btnConnect.AccessibleName = "Connect securely";
    }

    // Auto: try the fast presets, fastest past connect first; one that times out or drops is not retried until all others were tried.
    // A chosen preset (including the slow manual-only ones) is simply retried.
    private async Task RunAsync(CancellationToken ct)
    {
        var untried = new List<Preset>();
        var chosen = _protocolIndexes[cboProtocol.SelectedIndex]; // -1 = Auto; read once, a new pick applies on the next Connect
        var scan = ScanModes[cboScan.SelectedIndex];
        try
        {
            SetStatus("Downloading aether...", Palette.Connecting);
            if (!await Aether.EnsureInstalledAsync(Log, ct)) return;
            try { _ = MixedProxy.Start(Aether.ProxyPort, ct); }
            catch (System.Net.Sockets.SocketException) { Log($"Port {Aether.ProxyPort} is in use. Pick another in Core settings."); return; }
            while (true)
            {
                if (untried.Count == 0) untried.AddRange(chosen < 0 ? Aether.AutoOrder(_connectMs, Aether.SavedOrder) : [Aether.Presets[chosen]]);
                var p = untried[0];
                untried.RemoveAt(0);
                await ConnectAndMonitorAsync(p, scan, ct);
                await Task.Delay(1000, ct);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            KillProcess();
            SetStatus("Disconnected", Palette.Disconnected);
            lblDelay.Text = "Delay: -";
        }
    }

    // Returns when the preset fails to connect within the timeout, or after a connected session drops.
    private async Task ConnectAndMonitorAsync(Preset p, string scan, CancellationToken ct)
    {
        Log($"Trying {p.Name}...");
        SetStatus($"Connecting: {p.Name}", Palette.Connecting);
        var proc = _process = Aether.Start(p, Log, _coreOptions, scan);
        if (proc is null) return;

        var took = Stopwatch.StartNew();
        var ms = await Aether.WaitConnectedAsync(proc,
            _coreOptions.IsConfigured ? _coreOptions.StartupSeconds : p.StartupSecs, ct);
        _connectMs[Array.IndexOf(Aether.Presets, p)] = ms is null ? -1 : (int)took.ElapsedMilliseconds;
        SaveSetting("ConnectMs", Aether.FormatConnectMs(_connectMs));
        var order = Aether.AutoOrder(_connectMs, Aether.SavedOrder).Select(x => Array.IndexOf(Aether.Presets, x)).ToArray();
        Aether.SavedOrder = Aether.Rerank(order, _connectMs, Array.IndexOf(Aether.Presets, p));
        if (ms is null)
        {
            Log(proc.HasExited ? $"{p.Name}: process exited." : $"{p.Name}: no internet, switching.");
            KillProcess();
            return;
        }

        if (chkSysProxy.Checked) SystemProxy.Enable();
        SetStatus($"Connected: {p.Name}", Palette.Connected);
        Log($"Connected via {p.Name} in {took.Elapsed.TotalSeconds:0.0}s, delay {ms} ms.");
        // The core's own psiphon modes already run Psiphon; a second one on top only slows it down.
        if (chkPsiphon.Checked && !p.Args.Contains("psiphon")) _ = StartPsiphonAsync(ct); // a download must not hold up the connection monitor below
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

    private bool _psiphonStarting;

    // Opens psiphon3.exe from beside this exe once per connection, downloading it first if missing; skipped if already running.
    private async Task StartPsiphonAsync(CancellationToken ct)
    {
        if (_psiphonStarting || Process.GetProcessesByName("psiphon3").Length > 0) return;
        _psiphonStarting = true;
        try
        {
            if (!await Psiphon.EnsureDownloadedAsync(Log, ct) || ct.IsCancellationRequested) return;
        }
        catch (OperationCanceledException) { return; }
        finally { _psiphonStarting = false; }
        var exe = Psiphon.Exe;
        // Psiphon reads its Upstream Proxy setting from here at startup: point it at our HTTP port.
        using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Psiphon3"))
        {
            key.SetValue("SSHParentProxyHost", "127.0.0.1");
            key.SetValue("SSHParentProxyPort", Aether.ProxyPort);
            key.SetValue("SSHParentProxySkip", 0);
        }
        try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory }); }
        catch (Exception ex) { Log($"psiphon3: {ex.Message}"); }
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

    private void ShowCoreSettings()
    {
        if (_cts != null) return;
        using var dialog = new CoreSettingsForm(_coreOptions, _connectMs);
        if (dialog.ShowDialog(this) == DialogResult.OK) _coreOptions = dialog.Options;
    }

    // The startup check only logs what is new; the button also offers to install it.
    // The core is replaced only while disconnected, since a running aether.exe cannot be overwritten.
    private async Task CheckUpdatesAsync(bool interactive)
    {
        if (interactive) Log("Checking for updates...");
        Aether.Updates u;
        try { u = await Aether.CheckUpdatesAsync(CancellationToken.None); }
        catch (Exception ex) { if (interactive) Log($"Update check failed: {ex.Message}"); return; }
        if (IsDisposed) return;

        if (u.GuiNewer)
        {
            Log($"AetherGui {u.LatestGui} is available (you have {Aether.AppVersion}).", Color.Blue);
            if (interactive && u.GuiPage?.StartsWith("https://github.com/") == true && MessageBox.Show(this, $"AetherGui {u.LatestGui} is available. Open the download page?", "Update available",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                Process.Start(new ProcessStartInfo(u.GuiPage!) { UseShellExecute = true });
        }
        if (u.CoreNewer)
        {
            Log($"Core {u.LatestCore} is available (you have {u.Core?.ToString() ?? "none"}).", Color.Blue);
            if (!interactive) Log("Click 'Updates' to install it.");
            else if (_cts != null) Log("Disconnect first to update the core.");
            else if (MessageBox.Show(this, $"Update the core to {u.LatestCore}?", "Update available",
                         MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                btnConnect.Enabled = btnUpdates.Enabled = false;
                if (await Aether.InstallCoreAsync($"Updating the core to {u.LatestCore}...", Log, CancellationToken.None))
                    Log($"Core is now {await Aether.InstalledCoreVersionAsync(CancellationToken.None)}.");
                btnConnect.Enabled = btnUpdates.Enabled = true;
            }
        }
        if (interactive && !u.GuiNewer && !u.CoreNewer)
            Log($"Up to date: AetherGui {Aether.AppVersion}, core {u.Core?.ToString() ?? "not installed"}.");
    }

    private async Task SendCoreInputAsync()
    {
        var proc = _process;
        if (proc is null || proc.HasExited) { Log("Core is not running yet."); return; }
        using var dialog = new Form
        {
            Text = "Core input", ClientSize = new Size(440, 140), MinimumSize = new Size(360, 180),
            StartPosition = FormStartPosition.CenterParent, AutoScaleMode = AutoScaleMode.Dpi
        };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), RowCount = 3, ColumnCount = 1 };
        layout.Controls.Add(new Label { Text = "Code or prompt response (not logged)", AutoSize = true });
        var input = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Top };
        layout.Controls.Add(input);
        var send = new Button { Text = "Send", DialogResult = DialogResult.OK, AutoSize = true };
        layout.Controls.Add(send); dialog.Controls.Add(layout); dialog.AcceptButton = send;
        if (dialog.ShowDialog(this) != DialogResult.OK || input.Text.Length == 0) return;
        if (_process != proc) { Log("Core changed; input was not sent."); return; }
        try
        {
            if (proc.HasExited) { Log("Core exited; input was not sent."); return; }
            await proc.StandardInput.WriteLineAsync(input.Text);
            await proc.StandardInput.FlushAsync();
            Log("Input sent to core.");
        }
        catch { Log("Could not send input to core."); }
    }

    private void ShowProxyInfo()
    {
        var secrets = Directory.GetFiles(AppContext.BaseDirectory, "aether*.toml").Length;
        var head = Color.Blue; Color? info = null; var tip = Color.DarkOrange;
        Log("=== Proxy info ===", head);
        Log($"System proxy: {SystemProxy.Status()}", info);
        Log($"Proxy {Aether.ProxyPort} (HTTP + SOCKS5, system proxy) | core: SOCKS5 {Aether.SocksPort}, HTTP {Aether.CoreHttpPort}", info);
        Log($"aether.exe: {(_process is { HasExited: false } ? "running" : "stopped")} | {lblStatus.Text.TrimStart('●', ' ')} | {lblDelay.Text}", info);
        Log("Presets: " + string.Join(", ", Aether.Presets.Select(p => p.Name)), info);
        Log("Cleanup: form close, --watchdog copy, hidden PowerShell guard, reset on startup.", info);
        Log("=== Recommendations ===", head);
        Log($"1. {secrets} aether*.toml file(s) beside the exe hold private keys/tokens: keep this folder private, rotate keys if shared.", tip);
        Log("2. Enable 'Set system proxy' only when needed; anything with its own proxy settings can bypass it.", tip);
        Log("3. Auto rotates the fast protocols (30s each), or your own order from Core settings > Auto order. Tor/Psiphon help on strict networks.", tip);
    }

    private void SetStatus(string text, Color color)
    {
        lblStatus.Text = "\u25CF " + text;
        lblStatus.AccessibleName = "Connection status: " + text;
        lblStatus.ForeColor = color;
        prgConnecting.Visible = color == Palette.Connecting;
    }

    private void Log(string msg) => Log(msg, null);

    private void Log(string msg, Color? color)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => Log(msg, color)); } catch (InvalidOperationException) { }
            return;
        }
        txtLog.SelectionStart = txtLog.TextLength;
        txtLog.SelectionColor = color ?? txtLog.ForeColor;
        txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
        txtLog.SelectionColor = txtLog.ForeColor;
        txtLog.ScrollToCaret();
    }
}

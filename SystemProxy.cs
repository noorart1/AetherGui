using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace AetherGui;

// Windows system (WinINet) proxy pointing at aether's local SOCKS5 port.
static class SystemProxy
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    // Plain host:port is what Windows shows cleanly in Settings; it is read as an HTTP proxy.
    private static string Server => $"127.0.0.1:{Aether.HttpPort}";
    private const string Bypass = "<-loopback>;localhost;*.local;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*";

    private static readonly string AetherExe = Path.Combine(AppContext.BaseDirectory, "aether.exe").Replace("'", "''");

    [DllImport("wininet.dll")]
    private static extern bool InternetSetOption(IntPtr h, int option, IntPtr buf, int len);

    // Second, independent layer: a hidden PowerShell that waits for the GUI to die and then undoes the proxy.
    // It is not named AetherGui.exe and is detached via cmd's `start`, so `taskkill /IM AetherGui.exe`
    // and `taskkill /T` (kill the whole tree) that take out the GUI and the --watchdog copy do not reach it.
    public static void StartIndependentGuard(int pid)
    {
        var script = $@"
Wait-Process -Id {pid} -ErrorAction SilentlyContinue
Get-Process aether -ErrorAction SilentlyContinue | Where-Object {{ $_.Path -eq '{AetherExe}' }} | Stop-Process -Force
$k = 'HKCU:\{KeyPath}'
$port = (Get-ItemProperty 'HKCU:\Software\AetherGui' -ErrorAction SilentlyContinue).HttpPort
if (-not $port) {{ $port = {Aether.DefaultHttpPort} }} # read now: the port may have changed since this guard started
if ((Get-ItemProperty $k).ProxyServer -eq ""127.0.0.1:$port"") {{
    Set-ItemProperty $k ProxyEnable 0
    Add-Type -MemberDefinition '[DllImport(""wininet.dll"")] public static extern bool InternetSetOption(IntPtr h, int o, IntPtr b, int l);' -Name W -Namespace G
    [G.W]::InternetSetOption([IntPtr]::Zero, 39, [IntPtr]::Zero, 0) | Out-Null
    [G.W]::InternetSetOption([IntPtr]::Zero, 37, [IntPtr]::Zero, 0) | Out-Null
}}";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" /b powershell.exe -NoProfile -WindowStyle Hidden -EncodedCommand {encoded}")
        { CreateNoWindow = true, UseShellExecute = false });
    }

    public static void Enable() => Set(1, Server);

    public static string Status()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        var on = key?.GetValue("ProxyEnable") is 1;
        var srv = key?.GetValue("ProxyServer") as string;
        return on ? (srv == Server ? $"ON -> {srv} (ours)" : $"ON -> {srv} (NOT ours)") : "OFF";
    }

    // Only touches the setting if it is ours, so a user's own proxy is never cleared.
    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        if (key?.GetValue("ProxyServer") as string == Server) Set(0, Server);
    }

    private static void Set(int enable, string server)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true)!;
        key.SetValue("ProxyServer", server);
        key.SetValue("ProxyOverride", Bypass);
        key.SetValue("ProxyEnable", enable);
        InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0); // SETTINGS_CHANGED
        InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0); // REFRESH
    }
}

using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AetherGui;

// psiphon3.exe beside this exe: downloaded through the tunnel when missing, run only if Psiphon Inc. signed it.
static class Psiphon
{
    public static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "psiphon3.exe");
    private static readonly string[] Urls = ["https://psiphon.ca/psiphon3.exe", "https://s3.amazonaws.com/psiphon/web/mjr4-p23r-puwl/psiphon3.exe"];

    // Only the copy beside this exe, so a separately installed Psiphon is left alone.
    // Waits so SystemProxy.Disable right after sees it gone and clears the proxy it left behind.
    public static void Kill()
    {
        foreach (var p in Process.GetProcessesByName("psiphon3"))
            try { if (string.Equals(p.MainModule?.FileName, Exe, StringComparison.OrdinalIgnoreCase)) { p.Kill(true); p.WaitForExit(2000); } } catch { }
    }

    public static async Task<bool> EnsureDownloadedAsync(Action<string> log, CancellationToken ct)
    {
        if (File.Exists(Exe)) return true;
        var part = Exe + ".part";
        // psiphon.ca is often blocked, so fetch it through the tunnel that is already up.
        using var http = new HttpClient(new SocketsHttpHandler { Proxy = new WebProxy($"socks5://127.0.0.1:{Aether.SocksPort}") })
        { Timeout = TimeSpan.FromMinutes(5) };
        foreach (var url in Urls)
        {
            try
            {
                log($"psiphon3.exe not found, downloading from {new Uri(url).Host}...");
                await File.WriteAllBytesAsync(part, await http.GetByteArrayAsync(url, ct), ct);
                if (!IsSignedByPsiphon(part)) throw new Exception("not signed by Psiphon Inc., discarded");
                File.Move(part, Exe, true);
                log("psiphon3.exe downloaded and its signature verified.");
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log($"psiphon3 download failed: {ex.Message}"); }
            finally { File.Delete(part); }
        }
        return false;
    }

    // A valid Authenticode signature (WinVerifyTrust) whose signer is Psiphon Inc.
    public static bool IsSignedByPsiphon(string path)
    {
        try
        {
            if (!new X509Certificate2(X509Certificate.CreateFromSignedFile(path)).Subject.Contains("O=Psiphon Inc.")) return false;
        }
        catch (CryptographicException) { return false; } // no signature at all
        var file = new WinTrustFileInfo { cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(), pcwszFilePath = Marshal.StringToHGlobalUni(path) };
        var filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(file, filePtr, false);
            var data = new WinTrustData { cbStruct = (uint)Marshal.SizeOf<WinTrustData>(), dwUIChoice = 2 /* none */, dwUnionChoice = 1 /* file */, pFile = filePtr };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
            return WinVerifyTrust(IntPtr.Zero, ref action, ref data) == 0;
        }
        finally { Marshal.FreeHGlobal(filePtr); Marshal.FreeHGlobal(file.pcwszFilePath); }
    }

    [DllImport("wintrust.dll")]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid action, ref WinTrustData data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo { public uint cbStruct; public IntPtr pcwszFilePath, hFile, pgKnownSubject; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint cbStruct; public IntPtr pPolicyCallbackData, pSIPClientData;
        public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice; public IntPtr pFile;
        public uint dwStateAction; public IntPtr hWVTStateData, pwszURLReference;
        public uint dwProvFlags, dwUIContext; public IntPtr pSignatureSettings;
    }
}

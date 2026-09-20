using System.Diagnostics;

namespace AetherGui;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Watchdog mode: a tiny second copy of this exe that outlives a crashed/killed GUI and undoes its changes.
        if (args.Length == 2 && args[0] == "--watchdog")
        {
            try { Process.GetProcessById(int.Parse(args[1])).WaitForExit(); } catch { }
            foreach (var p in Process.GetProcessesByName("aether"))
                try { p.Kill(true); } catch { }
            SystemProxy.Disable();
            return;
        }

        // Single instance. Must come before anything that touches the proxy or aether.
        using var mutex = new Mutex(true, @"Local\AetherGui.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Aether GUI is already running.", "Aether GUI", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SystemProxy.Disable(); // leftover from a previous run that died with the machine (power loss, etc.)
        ApplicationConfiguration.Initialize();
        Application.Run(new Form1());
    }
}

using System.Net;
using System.Net.Sockets;

namespace AetherGui;

// One local port for both protocols: a SOCKS5 greeting starts with 0x05, anything else is HTTP.
// Each connection is handed to the core's matching listener byte for byte.
static class MixedProxy
{
    // Throws SocketException right away when the port is taken; otherwise serves until ct is cancelled.
    public static Task Start(int port, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        return AcceptAsync(listener, ct);
    }

    private static async Task AcceptAsync(TcpListener listener, CancellationToken ct)
    {
        try { while (true) _ = HandleAsync(await listener.AcceptTcpClientAsync(ct), ct); }
        catch (OperationCanceledException) { }
        finally { listener.Stop(); }
    }

    private static async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        using (var core = new TcpClient())
        {
            try
            {
                var app = client.GetStream();
                var first = new byte[1];
                if (await app.ReadAsync(first, ct) == 0) return;
                await core.ConnectAsync(IPAddress.Loopback, first[0] == 5 ? Aether.SocksPort : Aether.CoreHttpPort, ct);
                var tunnel = core.GetStream();
                await tunnel.WriteAsync(first, ct);
                // shortcut: either side closing ends both (no half-close); fine for browsers and CONNECT, revisit if a client needs half-close.
                await Task.WhenAny(app.CopyToAsync(tunnel, ct), tunnel.CopyToAsync(app, ct));
            }
            catch { } // a dropped connection or a core not up yet only ends this one connection
        }
    }
}

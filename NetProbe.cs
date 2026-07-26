using System.Net.Sockets;

namespace PrinterTool;

/// <summary>
/// Checks whether a printer answers on the network right now. The print server only
/// reports its own last-known state, which is often stale; a direct TCP connect to a
/// standard printing port tells us what is true at this moment.
/// </summary>
public static class NetProbe
{
    // RAW/JetDirect, IPP, LPD, embedded web server
    private static readonly int[] Ports = { 9100, 631, 515, 80 };

    public enum Result { Unknown, Reachable, NoResponse }

    public static async Task<Result> CheckAsync(string host, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host)) return Result.Unknown;

        // All ports at once; first success wins. Four sockets to one host is a
        // normal connection attempt, not a scan pattern.
        var attempts = Ports.Select(p => TryConnectAsync(host, p, ct)).ToList();
        while (attempts.Count > 0)
        {
            var done = await Task.WhenAny(attempts);
            if (done.Result) return Result.Reachable;
            attempts.Remove(done);
        }
        return Result.NoResponse;
    }

    private static async Task<bool> TryConnectAsync(string host, int port, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            var connect = client.ConnectAsync(host, port);
            var done = await Task.WhenAny(connect, Task.Delay(700, ct));
            if (done != connect) return false;          // timed out
            await connect;                              // surface connect errors
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}

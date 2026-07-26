using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PrinterTool;

/// <summary>
/// Minimal SNMPv1 GET-NEXT walker — no external packages. Read-only, community
/// "public", used solely to read the standard Printer-MIB supplies table
/// (1.3.6.1.2.1.43.11.1.1) for toner levels. Fails silently: if the printer's
/// IP can't be derived from the queue's port name, port 161 is filtered, or
/// SNMP is disabled on the device, the UI simply shows no toner data.
/// </summary>
public static class Snmp
{
    private static readonly Random _rng = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IPAddress?> DnsCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly int[] OidDesc  = { 1, 3, 6, 1, 2, 1, 43, 11, 1, 1, 6 };
    private static readonly int[] OidMax   = { 1, 3, 6, 1, 2, 1, 43, 11, 1, 1, 8 };
    private static readonly int[] OidLevel = { 1, 3, 6, 1, 2, 1, 43, 11, 1, 1, 9 };

    public sealed record Supply(string Description, int Max, int Level)
    {
        /// <summary>0..100, or -1 when the device reports unknown (-2) / unlimited.</summary>
        public int Percent => Max > 0 && Level >= 0 ? Math.Max(0, Math.Min(100, (int)Math.Round(100.0 * Level / Max))) : -1;
    }


    public sealed record Tray(string Name, int Percent);

    public sealed class DeviceInfo
    {
        public string Model = "";
        public string Serial = "";
        public long PageCount = -1;
        public long UptimeTicks = -1;      // hundredths of a second
        public string Display = "";
        public List<Tray> Trays = new();
        public List<Supply> Supplies = new();
    }

    // Printer MIB (RFC 3805) + standard MIB-II
    private static readonly int[] OidSysDescr   = { 1, 3, 6, 1, 2, 1, 1, 1 };
    private static readonly int[] OidSysUptime  = { 1, 3, 6, 1, 2, 1, 1, 3 };
    private static readonly int[] OidHrModel    = { 1, 3, 6, 1, 2, 1, 25, 3, 2, 1, 3 };   // hrDeviceDescr
    private static readonly int[] OidSerial     = { 1, 3, 6, 1, 2, 1, 43, 5, 1, 1, 17 };
    private static readonly int[] OidLifeCount  = { 1, 3, 6, 1, 2, 1, 43, 10, 2, 1, 4 };
    private static readonly int[] OidConsole    = { 1, 3, 6, 1, 2, 1, 43, 16, 5, 1, 2 };
    private static readonly int[] OidTrayName   = { 1, 3, 6, 1, 2, 1, 43, 8, 2, 1, 13 };
    private static readonly int[] OidTrayMax    = { 1, 3, 6, 1, 2, 1, 43, 8, 2, 1, 9 };
    private static readonly int[] OidTrayLevel  = { 1, 3, 6, 1, 2, 1, 43, 8, 2, 1, 10 };

    [Flags]
    public enum Needs
    {
        None = 0, Supplies = 1, Identity = 2, Counters = 4, Trays = 8, Display = 16,
        All = Supplies | Identity | Counters | Trays | Display,
    }

    /// <summary>One pass over a device, fetching only the parts the UI actually shows.
    /// v2c is tried first: with GETBULK and multi-varbind GET it needs 2-4 round trips
    /// per device instead of dozens, so results land near-instantly on a LAN.</summary>
    public static async Task<DeviceInfo?> QueryDeviceAsync(IEnumerable<string> hosts, Needs needs, CancellationToken ct)
    {
        if (needs == Needs.None) return null;
        foreach (var host in hosts)
        {
            if (string.IsNullOrWhiteSpace(host)) continue;
            var info = await QueryDeviceOneAsync(host, 1, needs, ct)
                    ?? await QueryDeviceOneAsync(host, 0, needs, ct);
            if (info is not null) return info;
            ct.ThrowIfCancellationRequested();
        }
        return null;
    }

    // Standard instance OIDs for the scalar values (index .0 / .1 / .1.1 per the MIBs)
    private static readonly int[] IidSysDescr  = { 1, 3, 6, 1, 2, 1, 1, 1, 0 };
    private static readonly int[] IidSysUptime = { 1, 3, 6, 1, 2, 1, 1, 3, 0 };
    private static readonly int[] IidModel     = { 1, 3, 6, 1, 2, 1, 25, 3, 2, 1, 3, 1 };
    private static readonly int[] IidSerial    = { 1, 3, 6, 1, 2, 1, 43, 5, 1, 1, 17, 1 };
    private static readonly int[] IidPages     = { 1, 3, 6, 1, 2, 1, 43, 10, 2, 1, 4, 1, 1 };
    private static readonly int[] IidConsole   = { 1, 3, 6, 1, 2, 1, 43, 16, 5, 1, 2, 1, 1 };

    /// <summary>Single round trip fetching all requested scalars at once (v2c only).</summary>
    private static async Task<Dictionary<string, object>?> GetScalarsAsync(
        IPAddress ip, List<int[]> oids, CancellationToken ct)
    {
        if (oids.Count == 0) return new Dictionary<string, object>();
        using var udp = new UdpClient(ip.AddressFamily);
        udp.Connect(ip, 161);
        byte[] packet = BuildGet(_rng.Next(1, int.MaxValue), oids, 1);
        await udp.SendAsync(packet, packet.Length);
        try
        {
            var recv = udp.ReceiveAsync();
            var done = await Task.WhenAny(recv, Task.Delay(700, ct));
            if (done != recv) return null;
            var binds = ParseResponseMulti(recv.Result.Buffer);
            if (binds is null) return null;
            var map = new Dictionary<string, object>();
            foreach (var (oid, value) in binds)
                map[string.Join(".", oid)] = value;
            return map;
        }
        catch { return null; }
    }

    private static T? Pick<T>(Dictionary<string, object>? scalars, int[] oid) where T : class
        => scalars is not null && scalars.TryGetValue(string.Join(".", oid), out var v) ? v as T : null;

    private static long PickLong(Dictionary<string, object>? scalars, int[] oid)
        => scalars is not null && scalars.TryGetValue(string.Join(".", oid), out var v) ? ToLong(v) : -1;

    private static async Task<DeviceInfo?> QueryDeviceOneAsync(string host, int version, Needs needs, CancellationToken ct)
    {
        try
        {
            IPAddress? ip = await ResolveAsync(host, ct);
            if (ip is null) return null;

            var info = new DeviceInfo();

            if (version >= 1)
            {
                // v2c fast path: every scalar in one GET — also serves as the liveness check
                var wanted = new List<int[]> { IidSysDescr };
                if (needs.HasFlag(Needs.Identity)) { wanted.Add(IidModel); wanted.Add(IidSerial); }
                if (needs.HasFlag(Needs.Counters)) { wanted.Add(IidSysUptime); wanted.Add(IidPages); }
                if (needs.HasFlag(Needs.Display)) wanted.Add(IidConsole);

                var scalars = await GetScalarsAsync(ip, wanted, ct);
                if (scalars is null) return null;    // no v2c answer — caller falls back to v1

                info.Model = Pick<string>(scalars, IidModel)
                             ?? (Pick<string>(scalars, IidSysDescr) ?? "").Split('\n', '\r')[0];
                info.Serial = Pick<string>(scalars, IidSerial) ?? "";
                info.Display = (Pick<string>(scalars, IidConsole) ?? "").Trim();
                info.UptimeTicks = PickLong(scalars, IidSysUptime);
                info.PageCount = PickLong(scalars, IidPages);
            }
            else
            {
                // v1: no per-varbind errors, so scalars must be walked individually
                var sys = await WalkAsync(ip, OidSysDescr, version, ct);
                if (sys is null || sys.Count == 0) return null;

                if (needs.HasFlag(Needs.Identity))
                {
                    info.Model = FirstString(await WalkAsync(ip, OidHrModel, version, ct))
                                 ?? FirstString(sys)?.Split('\n', '\r')[0] ?? "";
                    info.Serial = FirstString(await WalkAsync(ip, OidSerial, version, ct)) ?? "";
                }
                if (needs.HasFlag(Needs.Display))
                    info.Display = (FirstString(await WalkAsync(ip, OidConsole, version, ct)) ?? "").Trim();
                if (needs.HasFlag(Needs.Counters))
                {
                    var up = await WalkAsync(ip, OidSysUptime, version, ct);
                    info.UptimeTicks = up is { Count: > 0 } ? ToLong(up.Values.First()) : -1;
                    var pg = await WalkAsync(ip, OidLifeCount, version, ct);
                    info.PageCount = pg is { Count: > 0 } ? pg.Values.Select(ToLong).Max() : -1;
                }
            }

            // Tables: supplies and trays — GETBULK on v2c makes each one round trip
            Task<Dictionary<int, object>?> T(int[] oid) => WalkAsync(ip, oid, version, ct);
            var tDesc  = needs.HasFlag(Needs.Supplies) ? T(OidDesc) : null;
            var tMax   = needs.HasFlag(Needs.Supplies) ? T(OidMax) : null;
            var tLevel = needs.HasFlag(Needs.Supplies) ? T(OidLevel) : null;
            var tTName = needs.HasFlag(Needs.Trays) ? T(OidTrayName) : null;
            var tTMax  = needs.HasFlag(Needs.Trays) ? T(OidTrayMax) : null;
            var tTLev  = needs.HasFlag(Needs.Trays) ? T(OidTrayLevel) : null;

            var all = new[] { tDesc, tMax, tLevel, tTName, tTMax, tTLev }
                      .Where(t => t is not null)!.Cast<Task>().ToArray();
            if (all.Length > 0) await Task.WhenAll(all);

            if (tDesc is not null && await tDesc is { Count: > 0 } desc)
            {
                var max = await tMax! ?? new();
                var level = await tLevel! ?? new();
                foreach (var kv in desc)
                {
                    int m = max.TryGetValue(kv.Key, out var mv) ? ToInt(mv) : -1;
                    int l = level.TryGetValue(kv.Key, out var lv) ? ToInt(lv) : -1;
                    info.Supplies.Add(new Supply(kv.Value as string ?? "", m, l));
                }
            }

            if (tTMax is not null && await tTMax is { Count: > 0 } trayMax)
            {
                var tName = await tTName! ?? new();
                var tLev2 = await tTLev! ?? new();
                foreach (var kv in trayMax)
                {
                    int m = ToInt(kv.Value);
                    int l = tLev2.TryGetValue(kv.Key, out var lv) ? ToInt(lv) : -1;
                    if (m <= 0 || l < 0) continue;
                    string name = tName.TryGetValue(kv.Key, out var nv) ? nv as string ?? "" : "";
                    if (name.Length == 0) name = "Tray " + kv.Key;
                    info.Trays.Add(new Tray(name, Math.Max(0, Math.Min(100, (int)Math.Round(100.0 * l / m)))));
                }
            }

            // A v2c responder with zero printer data is likely not a printer — reject
            bool hasAny = info.Model.Length > 0 || info.Serial.Length > 0 || info.PageCount >= 0 ||
                          info.Supplies.Count > 0 || info.Trays.Count > 0 || info.Display.Length > 0 ||
                          info.UptimeTicks >= 0;
            return hasAny ? info : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstString(Dictionary<int, object>? d) =>
        d is { Count: > 0 } ? d.Values.OfType<string>().FirstOrDefault(v => v.Length > 0) : null;

    private static int ToInt(object v) => v is long l ? (int)l : v is int i ? i : -1;
    private static long ToLong(object v) => v is long l ? l : v is int i ? i : -1;

    private static async Task<IPAddress?> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var ip)) return ip;
        if (DnsCache.TryGetValue(host, out var cached)) return cached;
        try
        {
            var entry = await Dns.GetHostAddressesAsync(host);
            var result = entry.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                         ?? entry.FirstOrDefault();
            DnsCache[host] = result;
            return result;
        }
        catch
        {
            DnsCache[host] = null;
            return null;
        }
    }

    /// <summary>GET-NEXT walk of one column; returns map of last-OID-component (row index) → value.</summary>
    private static async Task<Dictionary<int, object>?> WalkAsync(IPAddress ip, int[] baseOid, int version, CancellationToken ct)
    {
        var result = new Dictionary<int, object>();
        using var udp = new UdpClient(ip.AddressFamily);
        udp.Client.ReceiveTimeout = 700;
        udp.Connect(ip, 161);

        int[] current = baseOid;
        int requestId = _rng.Next(1, int.MaxValue);

        for (int hop = 0; hop < 24; hop++)   // hard cap — these tables are tiny
        {
            byte[] packet = BuildGetNext(requestId + hop, current, version);
            await udp.SendAsync(packet, packet.Length);

            UdpReceiveResult resp;
            try
            {
                var recv = udp.ReceiveAsync();
                var done = await Task.WhenAny(recv, Task.Delay(700, ct));
                if (done != recv) return result.Count > 0 ? result : null;
                resp = recv.Result;
            }
            catch { return result.Count > 0 ? result : null; }

            var vb = ParseResponse(resp.Buffer);
            if (vb is null) return result.Count > 0 ? result : null;
            var (oid, value) = vb.Value;

            if (!StartsWith(oid, baseOid)) break;          // walked past the column
            result[oid[oid.Length - 1]] = value;
            current = oid;
        }
        return result;
    }

    private static bool OidEquals(int[] a, int[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static bool StartsWith(int[] oid, int[] prefix)
    {
        if (oid.Length < prefix.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
            if (oid[i] != prefix[i]) return false;
        return true;
    }

    // ---------- BER encoding ----------

    private static byte[] BuildPdu(byte pduTag, int requestId, int f1, int f2, IEnumerable<int[]> oids, int version)
    {
        var binds = oids.Select(oid => Seq(Concat(EncodeOid(oid), new byte[] { 0x05, 0x00 }))).ToArray();
        byte[] varList = Seq(Concat(binds));
        byte[] pdu = Tag(pduTag, Concat(EncodeInt(requestId), EncodeInt(f1), EncodeInt(f2), varList));
        byte[] community = EncodeOctets(Encoding.ASCII.GetBytes("public"));
        return Seq(Concat(EncodeInt(version), community, pdu));
    }

    private static byte[] BuildGetNext(int requestId, int[] oid, int version) =>
        BuildPdu(0xA1, requestId, 0, 0, new[] { oid }, version);

    /// <summary>SNMPv2c GETBULK — one round trip returns up to maxRep table rows.</summary>
    private static byte[] BuildGetBulk(int requestId, int[] oid, int maxRep) =>
        BuildPdu(0xA5, requestId, 0, maxRep, new[] { oid }, 1);

    /// <summary>Multi-varbind GET — several scalars fetched in a single round trip (v2c).</summary>
    public static byte[] BuildGet(int requestId, IEnumerable<int[]> oids, int version) =>
        BuildPdu(0xA0, requestId, 0, 0, oids, version);

    private static byte[] EncodeOid(int[] oid)
    {
        var body = new List<byte> { (byte)(40 * oid[0] + oid[1]) };
        for (int i = 2; i < oid.Length; i++)
        {
            int v = oid[i];
            if (v < 128) { body.Add((byte)v); continue; }
            var stack = new Stack<byte>();
            stack.Push((byte)(v & 0x7F));
            v >>= 7;
            while (v > 0) { stack.Push((byte)((v & 0x7F) | 0x80)); v >>= 7; }
            body.AddRange(stack);
        }
        return Tag(0x06, body.ToArray());
    }

    private static byte[] EncodeInt(int value)
    {
        var bytes = new List<byte>();
        uint v = (uint)value;
        do { bytes.Insert(0, (byte)(v & 0xFF)); v >>= 8; } while (v > 0);
        if ((bytes[0] & 0x80) != 0 && value >= 0) bytes.Insert(0, 0);
        return Tag(0x02, bytes.ToArray());
    }

    private static byte[] EncodeOctets(byte[] data) => Tag(0x04, data);
    private static byte[] Seq(byte[] content) => Tag(0x30, content);

    private static byte[] Tag(byte tag, byte[] content)
    {
        byte[] len = content.Length < 128
            ? new[] { (byte)content.Length }
            : content.Length < 256
                ? new byte[] { 0x81, (byte)content.Length }
                : new byte[] { 0x82, (byte)(content.Length >> 8), (byte)(content.Length & 0xFF) };
        return Concat(new[] { tag }, len, content);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var outp = new byte[parts.Sum(p => p.Length)];
        int off = 0;
        foreach (var p in parts) { Buffer.BlockCopy(p, 0, outp, off, p.Length); off += p.Length; }
        return outp;
    }

    // ---------- BER decoding ----------

    private static (int[] Oid, object Value)? ParseResponse(byte[] data)
    {
        var all = ParseResponseMulti(data);
        return all is { Count: > 0 } ? all[0] : null;
    }

    private static List<(int[] Oid, object Value)>? ParseResponseMulti(byte[] data)
    {
        try
        {
            int pos = 0;
            ReadHeader(data, ref pos, 0x30);            // message SEQUENCE
            SkipTlv(data, ref pos);                     // version
            SkipTlv(data, ref pos);                     // community
            ReadHeader(data, ref pos, 0xA2);            // GetResponse PDU
            SkipTlv(data, ref pos);                     // request-id
            int errStatus = ReadIntTlv(data, ref pos);  // error-status
            SkipTlv(data, ref pos);                     // error-index
            if (errStatus != 0) return null;
            int listLen = ReadHeader(data, ref pos, 0x30);   // varbind list
            int listEnd = pos + listLen;
            var result = new List<(int[], object)>();
            while (pos < listEnd)
            {
                ReadHeader(data, ref pos, 0x30);        // varbind
                int[] oid = ReadOidTlv(data, ref pos);
                object value = ReadValueTlv(data, ref pos);
                result.Add((oid, value));
            }
            return result;
        }
        catch { return null; }
    }

    private static int ReadHeader(byte[] d, ref int pos, byte expectedTag)
    {
        if (d[pos++] != expectedTag) throw new FormatException();
        return ReadLength(d, ref pos);
    }

    private static int ReadLength(byte[] d, ref int pos)
    {
        int len = d[pos++];
        if ((len & 0x80) == 0) return len;
        int n = len & 0x7F;
        len = 0;
        for (int i = 0; i < n; i++) len = (len << 8) | d[pos++];
        return len;
    }

    private static void SkipTlv(byte[] d, ref int pos)
    {
        pos++;
        int len = ReadLength(d, ref pos);
        pos += len;
    }

    private static int ReadIntTlv(byte[] d, ref int pos)
    {
        pos++;
        int len = ReadLength(d, ref pos);
        int v = (d[pos] & 0x80) != 0 ? -1 : 0;
        for (int i = 0; i < len; i++) v = (v << 8) | d[pos + i];
        pos += len;
        return v;
    }

    /// <summary>Reads a numeric TLV without sign-extending counters (they are unsigned).</summary>
    private static long ReadUnsignedTlv(byte[] d, ref int pos)
    {
        byte tag = d[pos++];
        int len = ReadLength(d, ref pos);
        long v = 0;
        bool signed = tag == 0x02 && len > 0 && (d[pos] & 0x80) != 0;
        if (signed) v = -1;
        for (int i = 0; i < len; i++) v = (v << 8) | d[pos + i];
        pos += len;
        return v;
    }

    private static int[] ReadOidTlv(byte[] d, ref int pos)
    {
        if (d[pos++] != 0x06) throw new FormatException();
        int len = ReadLength(d, ref pos);
        int end = pos + len;
        var oid = new List<int> { d[pos] / 40, d[pos] % 40 };
        pos++;
        while (pos < end)
        {
            int v = 0;
            while (true)
            {
                byte b = d[pos++];
                v = (v << 7) | (b & 0x7F);
                if ((b & 0x80) == 0) break;
            }
            oid.Add(v);
        }
        return oid.ToArray();
    }

    /// <summary>
    /// SNMP OCTET STRINGs carry no encoding tag. Devices send UTF-8 or Latin-1;
    /// decode strictly as UTF-8 and fall back to Latin-1 so umlauts survive.
    /// </summary>
    private static string DecodeText(byte[] d, int pos, int len)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(d, pos, len);
        }
        catch (ArgumentException)
        {
            return Encoding.GetEncoding(28591).GetString(d, pos, len);   // ISO-8859-1
        }
    }

    private static object ReadValueTlv(byte[] d, ref int pos)
    {
        byte tag = d[pos];
        // INTEGER, Counter32, Gauge32, TimeTicks, Opaque, Counter64 are all numeric
        if (tag == 0x02 || tag == 0x41 || tag == 0x42 || tag == 0x43 || tag == 0x46)
            return ReadUnsignedTlv(d, ref pos);
        pos++;
        int len = ReadLength(d, ref pos);
        object v = tag == 0x04 ? DecodeText(d, pos, len) : (object)Array.Empty<byte>();
        pos += len;
        return v;
    }
}

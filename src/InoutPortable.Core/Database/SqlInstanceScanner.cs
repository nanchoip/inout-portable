using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace InoutPortable.Core.Database;

/// <summary>A SQL Server instance discovered on the network via the SQL Server Resolution Protocol (SSRP).</summary>
public sealed record SqlInstanceInfo(string Server, string Instance, string? Version, int? TcpPort)
{
    public string DisplayName => $"{Server}\\{Instance}";
    public bool IsA3Erp => Instance.Equals("A3ERP", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Discovers SQL Server instances by talking to the SQL Server Browser service over UDP port 1434
/// (the same mechanism SSMS uses). Supports querying a specific host, broadcasting on the local
/// subnet, and sweeping the local /24. a3ERP instances are always named "A3ERP".
/// </summary>
public sealed class SqlInstanceScanner
{
    private const int BrowserPort = 1434;
    private static readonly byte[] UnicastRequest = { 0x03 };   // CLNT_UCAST_EX: list all instances on a host
    private static readonly byte[] BroadcastRequest = { 0x02 }; // CLNT_BCAST_EX: list instances on the subnet

    /// <summary>Full discovery: broadcast + local /24 sweep + any extra hosts/IPs supplied by the user.</summary>
    public async Task<IReadOnlyList<SqlInstanceInfo>> DiscoverAsync(
        IEnumerable<string>? extraHosts = null,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var found = new Dictionary<string, SqlInstanceInfo>(StringComparer.OrdinalIgnoreCase);

        void AddAll(IEnumerable<SqlInstanceInfo> items)
        {
            foreach (var i in items)
                found[$"{i.Server}\\{i.Instance}"] = i;
        }

        progress?.Report("Difusión en la red local (broadcast)…");
        AddAll(await ScanBroadcastAsync(1500, ct));

        progress?.Report("Explorando la subred local…");
        AddAll(await ScanLocalSubnetAsync(700, 64, ct));

        if (extraHosts is not null)
        {
            foreach (var host in extraHosts.Where(h => !string.IsNullOrWhiteSpace(h)))
            {
                progress?.Report($"Consultando {host}…");
                AddAll(await ScanHostAsync(host.Trim(), 900, ct));
            }
        }

        // a3ERP instances first, then by server.
        return found.Values
            .OrderByDescending(i => i.IsA3Erp)
            .ThenBy(i => i.Server)
            .ThenBy(i => i.Instance)
            .ToList();
    }

    /// <summary>Queries a single host for the instances it exposes.</summary>
    public async Task<IReadOnlyList<SqlInstanceInfo>> ScanHostAsync(string host, int timeoutMs = 900, CancellationToken ct = default)
    {
        try
        {
            using var udp = new UdpClient();
            udp.Connect(host, BrowserPort);
            await udp.SendAsync(UnicastRequest, ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            var res = await udp.ReceiveAsync(cts.Token);
            return ParseResponse(res.Buffer, host);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Array.Empty<SqlInstanceInfo>(); }
        catch (SocketException) { return Array.Empty<SqlInstanceInfo>(); }
    }

    /// <summary>Broadcasts on the local subnet and collects replies until the timeout elapses.</summary>
    public async Task<IReadOnlyList<SqlInstanceInfo>> ScanBroadcastAsync(int timeoutMs = 1500, CancellationToken ct = default)
    {
        var results = new Dictionary<string, SqlInstanceInfo>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            await udp.SendAsync(BroadcastRequest, new IPEndPoint(IPAddress.Broadcast, BrowserPort), ct);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var res = await udp.ReceiveAsync(cts.Token);
                    var server = res.RemoteEndPoint.Address.ToString();
                    foreach (var info in ParseResponse(res.Buffer, server))
                        results[info.DisplayName] = info;
                }
                catch (OperationCanceledException) { break; }
            }
        }
        catch (SocketException) { }
        return results.Values.ToList();
    }

    /// <summary>Sweeps every host on the local /24 subnet(s) in parallel via unicast.</summary>
    public async Task<IReadOnlyList<SqlInstanceInfo>> ScanLocalSubnetAsync(int timeoutMs = 700, int concurrency = 64, CancellationToken ct = default)
    {
        var hosts = EnumerateLocalSubnetHosts().ToList();
        if (hosts.Count == 0) return Array.Empty<SqlInstanceInfo>();

        var results = new List<SqlInstanceInfo>();
        using var gate = new SemaphoreSlim(concurrency);

        var tasks = hosts.Select(async host =>
        {
            await gate.WaitAsync(ct);
            try { return await ScanHostAsync(host, timeoutMs, ct); }
            finally { gate.Release(); }
        });

        foreach (var found in await Task.WhenAll(tasks))
            results.AddRange(found);

        return results;
    }

    /// <summary>Parses an SSRP SVR_RESP datagram into instance records. Public for testing.</summary>
    public static List<SqlInstanceInfo> ParseResponse(byte[] buffer, string server)
    {
        var list = new List<SqlInstanceInfo>();
        if (buffer.Length < 3 || buffer[0] != 0x05)
            return list;

        int dataLen = buffer[1] | (buffer[2] << 8);
        int available = Math.Min(dataLen, buffer.Length - 3);
        if (available <= 0) return list;

        string payload = Encoding.ASCII.GetString(buffer, 3, available);

        foreach (var block in payload.Split(new[] { ";;" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var tokens = block.Split(';');
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i + 1 < tokens.Length; i += 2)
                map[tokens[i]] = tokens[i + 1];

            if (!map.TryGetValue("InstanceName", out var instance) || string.IsNullOrWhiteSpace(instance))
                continue;

            map.TryGetValue("Version", out var version);
            int? port = map.TryGetValue("tcp", out var tcp) && int.TryParse(tcp, out var p) ? p : null;

            list.Add(new SqlInstanceInfo(server, instance, version, port));
        }

        return list;
    }

    private static IEnumerable<string> EnumerateLocalSubnetHosts()
    {
        var seen = new HashSet<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up ||
                ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                var bytes = ua.Address.GetAddressBytes();
                var prefix = $"{bytes[0]}.{bytes[1]}.{bytes[2]}.";
                if (!seen.Add(prefix)) continue;

                for (int host = 1; host <= 254; host++)
                {
                    if (host == bytes[3]) continue; // skip self
                    yield return prefix + host;
                }
            }
        }
    }
}

using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace DXMirror.App.Services;

/// <summary>Result of the discovery health check.</summary>
public sealed record DiscoveryCheck(
    bool BonjourInstalled,
    bool BonjourRunning,
    bool BonjourOnAdapter,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> OtherAirPlayReceivers)
{
    public bool HasProblem => !BonjourInstalled || !BonjourRunning || !BonjourOnAdapter || OtherAirPlayReceivers.Count > 0;
}

/// <summary>
/// Finds programs that prevent iPhones from discovering DXMirror: anything that holds UDP 5353 (mDNS)
/// on the selected network address so that Apple Bonjour cannot answer there (seen with TeamViewer),
/// and other AirPlay receivers listening on the classic AirPlay ports. Uses the Windows IP helper API
/// (GetExtendedUdpTable / GetExtendedTcpTable) to map sockets to processes.
/// </summary>
internal static class NetworkConflicts
{
    public const string BonjourDownloadUrl = "https://support.apple.com/de-de/106380";

    private static readonly HashSet<string> HarmlessMdnsProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        // Windows' own DNS client shares 5353; DXMirror's receiver; Bonjour itself
        "svchost", "uxplay", "mDNSResponder", "System", "Idle",
    };

    private static readonly int[] AirPlayTcpPorts = [7000, 7001, 7100];

    public static DiscoveryCheck Check(string? adapterIPv4)
    {
        bool installed = File.Exists(Path.Combine(Environment.SystemDirectory, "dnssd.dll"));
        var udp = GetUdpListeners();
        var names = new Dictionary<int, string>();
        string Name(int pid)
        {
            if (names.TryGetValue(pid, out var n)) return n;
            try { using var p = Process.GetProcessById(pid); n = p.ProcessName; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { n = "PID " + pid; }
            names[pid] = n;
            return n;
        }

        var mdns = udp.Where(e => e.Port == 5353).ToList();
        bool running = mdns.Any(e => Name(e.Pid).Equals("mDNSResponder", StringComparison.OrdinalIgnoreCase))
                       || Process.GetProcessesByName("mDNSResponder").Length > 0;

        bool onAdapter = true;
        var blockers = new List<string>();
        if (adapterIPv4 is not null && IPAddress.TryParse(adapterIPv4, out var ip))
        {
            onAdapter = mdns.Any(e => Name(e.Pid).Equals("mDNSResponder", StringComparison.OrdinalIgnoreCase)
                                      && (e.Address.Equals(ip) || e.Address.Equals(IPAddress.Any)));
            if (!onAdapter)
            {
                blockers.AddRange(mdns
                    .Where(e => e.Address.Equals(ip) && !HarmlessMdnsProcesses.Contains(Name(e.Pid)))
                    .Select(e => Name(e.Pid))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            }
        }

        var receivers = GetTcpListeners()
            .Where(e => AirPlayTcpPorts.Contains(e.Port))
            .Select(e => Name(e.Pid))
            .Where(n => !n.Equals("uxplay", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("PID ", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new DiscoveryCheck(installed, running, onAdapter, blockers, receivers);
    }

    // ------------------------------------------------------------------ IP helper interop
    private readonly record struct Endpoint(IPAddress Address, int Port, int Pid);

    private const int AF_INET = 2;
    private const int UDP_TABLE_OWNER_PID = 1;
    private const int TCP_TABLE_OWNER_PID_LISTENER = 3;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(nint table, ref int size, bool sort, int af, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(nint table, ref int size, bool sort, int af, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_UDPROW_OWNER_PID
    {
        public uint LocalAddr;
        public uint LocalPort;
        public uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    private static int PortFromNetwork(uint p) => (int)(((p & 0xFF) << 8) | ((p >> 8) & 0xFF));

    private static List<Endpoint> GetUdpListeners() =>
        ReadTable<MIB_UDPROW_OWNER_PID>(
            (nint t, ref int s) => GetExtendedUdpTable(t, ref s, false, AF_INET, UDP_TABLE_OWNER_PID, 0),
            r => new Endpoint(new IPAddress(r.LocalAddr), PortFromNetwork(r.LocalPort), (int)r.OwningPid));

    private static List<Endpoint> GetTcpListeners() =>
        ReadTable<MIB_TCPROW_OWNER_PID>(
            (nint t, ref int s) => GetExtendedTcpTable(t, ref s, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0),
            r => new Endpoint(new IPAddress(r.LocalAddr), PortFromNetwork(r.LocalPort), (int)r.OwningPid));

    private delegate uint TableReader(nint table, ref int size);

    private static List<Endpoint> ReadTable<TRow>(TableReader read, Func<TRow, Endpoint> map) where TRow : struct
    {
        var result = new List<Endpoint>();
        int size = 0;
        read(0, ref size);
        if (size <= 0) return result;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (read(buffer, ref size) != 0) return result;
            int count = Marshal.ReadInt32(buffer);
            int rowSize = Marshal.SizeOf<TRow>();
            var ptr = buffer + 4;
            for (int i = 0; i < count; i++)
            {
                result.Add(map(Marshal.PtrToStructure<TRow>(ptr + i * rowSize)));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }
}

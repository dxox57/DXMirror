using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DXMirror.Core.Network;

public enum AdapterKind
{
    Ethernet,
    WiFi,
    Virtual,
    Vpn,
    Other,
}

public sealed record NetworkAdapterInfo(
    string Id,
    string Name,
    string Description,
    AdapterKind Kind,
    bool IsUp,
    bool HasGateway,
    IReadOnlyList<string> IPv4Addresses,
    IReadOnlyList<string> IPv6Addresses,
    long SpeedBitsPerSecond)
{
    public string? PrimaryIPv4 => IPv4Addresses.Count > 0 ? IPv4Addresses[0] : null;

    /// <summary>Higher is better: physical, up, has gateway, has private IPv4.</summary>
    public int SuitabilityScore
    {
        get
        {
            if (!IsUp || PrimaryIPv4 is null) return 0;
            int score = 10;
            score += Kind switch { AdapterKind.Ethernet => 40, AdapterKind.WiFi => 35, AdapterKind.Other => 10, _ => 0 };
            if (HasGateway) score += 30;
            if (NetworkAdapterService.IsPrivateIPv4(PrimaryIPv4)) score += 15;
            if (Kind is AdapterKind.Vpn or AdapterKind.Virtual) score -= 5;
            return Math.Max(score, 1);
        }
    }
}

/// <summary>
/// Enumerates network adapters, classifies them (Ethernet / WLAN / virtual / VPN), selects the
/// most suitable one for AirPlay and raises <see cref="Changed"/> when the network changes.
/// </summary>
public sealed class NetworkAdapterService : IDisposable
{
    private static readonly string[] VirtualMarkers =
        ["Hyper-V", "vEthernet", "VirtualBox", "VMware", "WSL", "Virtual", "Loopback", "Npcap", "Docker", "Bluetooth"];
    private static readonly string[] VpnMarkers =
        ["VPN", "TAP-", "TUN", "WireGuard", "OpenVPN", "Tailscale", "ZeroTier", "Cisco AnyConnect", "Fortinet", "NordLynx", "Hamachi", "PANGP"];

    private readonly Timer _debounce;
    private bool _disposed;

    public event EventHandler? Changed;

    public NetworkAdapterService()
    {
        _debounce = new Timer(_ => Changed?.Invoke(this, EventArgs.Empty), null, Timeout.Infinite, Timeout.Infinite);
        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;
        // Windows fires several events per change; debounce to one notification
        _debounce.Change(TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    public static IReadOnlyList<NetworkAdapterInfo> GetAdapters()
    {
        var result = new List<NetworkAdapterInfo>();
        NetworkInterface[] nics;
        try { nics = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException) { return result; }

        foreach (var nic in nics)
        {
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            IPInterfaceProperties props;
            try { props = nic.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }

            var v4 = new List<string>();
            var v6 = new List<string>();
            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                {
                    if (!ua.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal)) v4.Add(ua.Address.ToString());
                }
                else if (ua.Address.AddressFamily == AddressFamily.InterNetworkV6 && !ua.Address.IsIPv6Multicast)
                {
                    v6.Add(ua.Address.ToString());
                }
            }
            bool hasGw = props.GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any));

            long speed;
            try { speed = nic.Speed; } catch (PlatformNotSupportedException) { speed = 0; }

            result.Add(new NetworkAdapterInfo(
                nic.Id, nic.Name, nic.Description,
                Classify(nic.NetworkInterfaceType, nic.Name, nic.Description),
                nic.OperationalStatus == OperationalStatus.Up,
                hasGw, v4, v6, speed));
        }
        return result.OrderByDescending(a => a.SuitabilityScore).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static AdapterKind Classify(NetworkInterfaceType type, string name, string description)
    {
        var text = name + " " + description;
        if (VpnMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase)) || type == NetworkInterfaceType.Ppp)
            return AdapterKind.Vpn;
        if (VirtualMarkers.Any(m => text.Contains(m, StringComparison.OrdinalIgnoreCase)))
            return AdapterKind.Virtual;
        return type switch
        {
            NetworkInterfaceType.Wireless80211 => AdapterKind.WiFi,
            NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT
                or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => AdapterKind.Ethernet,
            _ => AdapterKind.Other,
        };
    }

    /// <summary>Choose the adapter used for mDNS advertising.</summary>
    public static NetworkAdapterInfo? Select(IReadOnlyList<NetworkAdapterInfo> adapters, bool automatic, string? preferredId)
    {
        if (!automatic && preferredId is not null)
        {
            var chosen = adapters.FirstOrDefault(a => a.Id == preferredId);
            if (chosen is { IsUp: true, PrimaryIPv4: not null }) return chosen;
            // fall through to automatic choice if the preferred adapter is gone / down
        }
        return adapters.Where(a => a.SuitabilityScore > 0).OrderByDescending(a => a.SuitabilityScore).FirstOrDefault();
    }

    public static bool IsPrivateIPv4(string? address)
    {
        if (!IPAddress.TryParse(address, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168)
               || (b[0] == 100 && b[1] >= 64 && b[1] <= 127); // CGNAT – also used by some routers
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _debounce.Dispose();
    }
}

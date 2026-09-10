using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using NEXUS.Models;

namespace NEXUS.Services
{
    /// <summary>
    /// Reports on the local machine's own network position and, on explicit request,
    /// reads the machine's ARP table to list neighbours it has already spoken to.
    ///
    /// Deliberately does NOT sweep or probe address ranges: everything here is a read
    /// of state this machine already holds about its own connections.
    /// </summary>
    public class NetworkManager
    {
        private readonly DataStore _store;
        private readonly EventBus _bus;
        private readonly DeviceManager _devices;

        public NetworkManager(DataStore store, EventBus bus, DeviceManager devices)
        {
            _store = store;
            _bus = bus;
            _devices = devices;
        }

        public ObservableCollection<NetworkInterfaceInfo> Interfaces { get; } =
            new ObservableCollection<NetworkInterfaceInfo>();

        public event Action Changed;

        public bool IsConnected { get; private set; }
        public string PrimaryAddress { get; private set; } = "\u2014";
        public string PrimaryInterface { get; private set; } = "\u2014";
        public string Gateway { get; private set; } = "\u2014";
        public string HostName { get; private set; } = Environment.MachineName;
        public string StatusLabel => IsConnected ? "CONNECTED" : "OFFLINE";
        public string StatusBrushKey => IsConnected ? "BrushCompleted" : "BrushCritical";

        public long BytesSent { get; private set; }
        public long BytesReceived { get; private set; }

        public string ThroughputDisplay => Format(BytesReceived) + " IN / " + Format(BytesSent) + " OUT";

        /// <summary>Round-trip time to this machine's own default gateway, in milliseconds.</summary>
        public long LatencyMs { get; private set; } = -1;

        public string LatencyDisplay => LatencyMs < 0 ? "\u2014" : LatencyMs + " MS";

        public string LatencyBrushKey => LatencyMs < 0 ? "BrushTextMuted"
            : LatencyMs < 20 ? "BrushCompleted"
            : LatencyMs < 80 ? "BrushNormal" : "BrushHigh";

        /// <summary>
        /// Measures the gateway round trip. This is a single ping to the router this
        /// machine is already using, so it stays inside the operator's own network.
        /// </summary>
        public void MeasureLatency()
        {
            if (string.IsNullOrWhiteSpace(Gateway) || Gateway == "\u2014")
            {
                LatencyMs = -1;
                return;
            }

            try
            {
                using var ping = new Ping();
                var reply = ping.Send(Gateway, 1200);
                LatencyMs = reply != null && reply.Status == IPStatus.Success ? reply.RoundtripTime : -1;
            }
            catch
            {
                LatencyMs = -1;
            }

            Changed?.Invoke();
        }

        /// <summary>Reads the machine's own adapters. Safe to call on a timer.</summary>
        public void Refresh(bool quiet = true)
        {
            try
            {
                IsConnected = NetworkInterface.GetIsNetworkAvailable();
                Interfaces.Clear();

                long sent = 0, received = 0;
                NetworkInterfaceInfo primary = null;

                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    var props = nic.GetIPProperties();
                    var ipv4 = props.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

                    var info = new NetworkInterfaceInfo
                    {
                        Name = nic.Name,
                        Description = nic.Description,
                        IpAddress = ipv4?.Address.ToString() ?? "",
                        MacAddress = FormatMac(nic.GetPhysicalAddress()),
                        IsUp = nic.OperationalStatus == OperationalStatus.Up,
                        Speed = nic.Speed > 0 ? (nic.Speed / 1_000_000) + " Mbps" : "",
                        Link = nic.NetworkInterfaceType switch
                        {
                            NetworkInterfaceType.Ethernet => LinkKind.Ethernet,
                            NetworkInterfaceType.GigabitEthernet => LinkKind.Ethernet,
                            NetworkInterfaceType.FastEthernetT => LinkKind.Ethernet,
                            NetworkInterfaceType.Wireless80211 => LinkKind.WiFi,
                            NetworkInterfaceType.Loopback => LinkKind.Loopback,
                            _ => LinkKind.Unknown
                        }
                    };

                    Interfaces.Add(info);

                    if (nic.OperationalStatus == OperationalStatus.Up &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        try
                        {
                            var stats = nic.GetIPStatistics();
                            sent += stats.BytesSent;
                            received += stats.BytesReceived;
                        }
                        catch { }

                        if (primary == null && !string.IsNullOrEmpty(info.IpAddress))
                        {
                            primary = info;
                            var gw = props.GatewayAddresses
                                .FirstOrDefault(g => g.Address != null &&
                                    g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                            if (gw != null) Gateway = gw.Address.ToString();
                        }
                    }
                }

                BytesSent = sent;
                BytesReceived = received;

                if (primary != null)
                {
                    PrimaryAddress = primary.IpAddress;
                    PrimaryInterface = primary.Name;
                }

                if (!quiet)
                    _bus.Publish(EventCategory.Network, "Network state refreshed",
                        PrimaryInterface + " \u00B7 " + PrimaryAddress, EventSeverity.Info, "NETWORK");
            }
            catch (Exception ex)
            {
                IsConnected = false;
                _bus.Publish(EventCategory.Network, "Network query failed", ex.Message,
                             EventSeverity.Warning, "NETWORK");
            }

            Changed?.Invoke();
        }

        /// <summary>
        /// Lists neighbours from this machine's own ARP cache and registers any that are
        /// not already known. This reads local state only — it sends no probes and touches
        /// nothing outside the network this machine is already joined to.
        /// </summary>
        public int DiscoverFromArpTable()
        {
            var found = 0;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    _bus.Publish(EventCategory.Network, "Discovery unavailable",
                        "The ARP utility could not be started.", EventSeverity.Warning, "NETWORK");
                    return 0;
                }

                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(4000);

                var pattern = new Regex(@"(?<ip>\d{1,3}(?:\.\d{1,3}){3})\s+(?<mac>[0-9a-fA-F]{2}(?:[-:][0-9a-fA-F]{2}){5})");

                foreach (Match m in pattern.Matches(output))
                {
                    var ip = m.Groups["ip"].Value;
                    var mac = m.Groups["mac"].Value.Replace('-', ':').ToUpperInvariant();

                    // Skip broadcast and multicast entries.
                    if (ip.EndsWith(".255") || ip.StartsWith("224.") || ip.StartsWith("239.")) continue;
                    if (mac.StartsWith("FF:FF") || mac.StartsWith("01:00:5E")) continue;

                    var existing = _devices.Devices.FirstOrDefault(d =>
                        string.Equals(d.IpAddress, ip, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(d.MacAddress) &&
                         string.Equals(d.MacAddress, mac, StringComparison.OrdinalIgnoreCase)));

                    if (existing != null)
                    {
                        existing.MacAddress = mac;
                        existing.LastSeenUtc = DateTime.UtcNow;
                        continue;
                    }

                    _devices.Devices.Add(new Device
                    {
                        Name = "Neighbour " + ip.Substring(ip.LastIndexOf('.') + 1),
                        Type = DeviceType.Unknown,
                        Link = LinkKind.Unknown,
                        IpAddress = ip,
                        MacAddress = mac,
                        Status = DeviceStatus.Online,
                        Integration = IntegrationState.Observed,
                        IntegrationId = "",
                        IsDiscovered = true,
                        ParentName = "Router",
                        LastSeenUtc = DateTime.UtcNow
                    });
                    found++;
                }

                _bus.Publish(EventCategory.Network, "Local neighbour scan complete",
                    found == 0
                        ? "No new hosts in this machine's ARP cache."
                        : found + " new host(s) registered from the local ARP cache.",
                    EventSeverity.Notice, "NETWORK");

                _devices.Touch();
            }
            catch (Exception ex)
            {
                _bus.Publish(EventCategory.Network, "Discovery failed", ex.Message,
                             EventSeverity.Warning, "NETWORK");
            }

            Changed?.Invoke();
            return found;
        }

        /// <summary>Single-host reachability check, used by the terminal's ping command.</summary>
        public string Ping(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return "No host supplied.";

            try
            {
                using var ping = new Ping();
                var reply = ping.Send(host, 2000);

                var text = reply != null && reply.Status == IPStatus.Success
                    ? "Reply from " + reply.Address + " in " + reply.RoundtripTime + " ms"
                    : "No reply (" + (reply?.Status.ToString() ?? "timeout") + ")";

                _bus.Publish(EventCategory.Network, "Reachability check", host + " \u00B7 " + text,
                             EventSeverity.Info, "NETWORK");
                return text;
            }
            catch (Exception ex)
            {
                return "Ping failed: " + ex.Message;
            }
        }

        private static string FormatMac(PhysicalAddress address)
        {
            if (address == null) return "";
            var bytes = address.GetAddressBytes();
            if (bytes.Length == 0) return "";
            return string.Join(":", bytes.Select(b => b.ToString("X2")));
        }

        private static string Format(long bytes)
        {
            if (bytes > 1_000_000_000) return Math.Round(bytes / 1_000_000_000.0, 1) + " GB";
            if (bytes > 1_000_000) return Math.Round(bytes / 1_000_000.0, 1) + " MB";
            if (bytes > 1_000) return Math.Round(bytes / 1_000.0, 1) + " KB";
            return bytes + " B";
        }
    }
}

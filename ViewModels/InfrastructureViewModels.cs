using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using NEXUS.Common;
using NEXUS.Models;
using NEXUS.Services;

namespace NEXUS.ViewModels
{
    /// <summary>One node in the rendered topology tree.</summary>
    public class TopologyNode : ObservableObject
    {
        public string Name { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Glyph { get; set; } = "UnknownNode";
        public string BrushKey { get; set; } = "BrushCompleted";
        public bool IsRoot { get; set; }
        public Device Device { get; set; }
        public ObservableCollection<TopologyNode> Children { get; } = new ObservableCollection<TopologyNode>();
    }

    public class NetworkViewModel : ViewModelBase
    {
        private readonly NetworkManager _network;
        private readonly DeviceManager _devices;
        private string _pingHost = "";
        private string _pingResult = "";

        public NetworkViewModel(ShellViewModel shell)
        {
            Shell = shell;
            _network = AppServices.Network;
            _devices = AppServices.Devices;

            RefreshCommand = new RelayCommand(_ => { _network.Refresh(quiet: false); Rebuild(); });
            DiscoverCommand = new RelayCommand(_ => RunDiscovery());
            PingCommand = new RelayCommand(_ => RunPing(), _ => !string.IsNullOrWhiteSpace(PingHost));
            OpenDeviceCommand = new RelayCommand(p =>
            {
                if (p is TopologyNode node && node.Device != null) Shell.OpenDevice(node.Device);
            });

            _network.Changed += Rebuild;
            _devices.Changed += Rebuild;
            Rebuild();
        }

        public ShellViewModel Shell { get; }

        public ObservableCollection<TopologyNode> Topology { get; } = new ObservableCollection<TopologyNode>();
        public ObservableCollection<NetworkInterfaceInfo> Interfaces => _network.Interfaces;

        public ICommand RefreshCommand { get; }
        public ICommand DiscoverCommand { get; }
        public ICommand PingCommand { get; }
        public ICommand OpenDeviceCommand { get; }

        public string StatusLabel => _network.StatusLabel;
        public string StatusBrushKey => _network.StatusBrushKey;
        public string HostName => _network.HostName;
        public string PrimaryAddress => _network.PrimaryAddress;
        public string PrimaryInterface => _network.PrimaryInterface;
        public string Gateway => _network.Gateway;
        public string ThroughputDisplay => _network.ThroughputDisplay;
        public int NodeCount => _devices.Total;
        public int OnlineCount => _devices.OnlineCount;

        public string PingHost
        {
            get => _pingHost;
            set => Set(ref _pingHost, value);
        }

        public string PingResult
        {
            get => _pingResult;
            private set => Set(ref _pingResult, value);
        }

        public override void OnActivated() { _network.Refresh(); Rebuild(); }

        public override void OnTick()
            => OnPropertiesChanged(nameof(ThroughputDisplay), nameof(StatusLabel), nameof(StatusBrushKey));

        private void RunDiscovery()
        {
            var added = _network.DiscoverFromArpTable();
            AppServices.Toasts.Show("NEIGHBOUR SCAN COMPLETE",
                added == 0 ? "No new hosts in this machine's ARP cache." : added + " host(s) registered.",
                ToastKind.Success);
            Rebuild();
        }

        private void RunPing()
        {
            PingResult = _network.Ping(PingHost.Trim());
        }

        /// <summary>Builds the tree from each device's declared parent, rooted at NEXUS CORE.</summary>
        private void Rebuild()
        {
            Topology.Clear();

            var internet = new TopologyNode
            {
                Name = "INTERNET",
                Detail = _network.IsConnected ? "UPLINK PRESENT" : "NO UPLINK",
                Glyph = "Globe",
                BrushKey = _network.IsConnected ? "BrushCompleted" : "BrushCritical",
                IsRoot = true
            };

            var root = new TopologyNode
            {
                Name = "NEXUS CORE",
                Detail = HostName + " \u00B7 " + PrimaryAddress + " \u00B7 RTT " + _network.LatencyDisplay,
                Glyph = "Command",
                BrushKey = "BrushAccent",
                IsRoot = true
            };

            var byName = _devices.Devices.ToList();
            var roots = byName.Where(d => string.IsNullOrWhiteSpace(d.ParentName)).ToList();
            if (roots.Count == 0) roots = byName.Take(1).ToList();

            foreach (var device in roots)
                root.Children.Add(BuildNode(device, byName, 0));

            // Anything whose declared parent no longer exists still gets shown.
            foreach (var orphan in byName.Where(d =>
                         !string.IsNullOrWhiteSpace(d.ParentName) &&
                         !byName.Any(p => string.Equals(p.Name, d.ParentName, StringComparison.OrdinalIgnoreCase))))
                root.Children.Add(BuildNode(orphan, byName, 0));

            internet.Children.Add(root);
            Topology.Add(internet);

            OnPropertiesChanged(nameof(StatusLabel), nameof(StatusBrushKey), nameof(PrimaryAddress),
                nameof(PrimaryInterface), nameof(Gateway), nameof(ThroughputDisplay),
                nameof(NodeCount), nameof(OnlineCount));
        }

        private TopologyNode BuildNode(Device device, List<Device> all, int depth)
        {
            var node = new TopologyNode
            {
                Name = device.Name,
                Detail = device.AddressDisplay + " \u00B7 " + device.LinkLabel + " \u00B7 " + device.StatusLabel,
                Glyph = device.Glyph,
                BrushKey = device.StatusBrushKey,
                Device = device
            };

            if (depth > 4) return node;

            foreach (var child in all.Where(c =>
                         string.Equals(c.ParentName, device.Name, StringComparison.OrdinalIgnoreCase)))
                node.Children.Add(BuildNode(child, all, depth + 1));

            return node;
        }
    }

    /// <summary>Live telemetry for the machine NEXUS runs on, plus useful local shortcuts.</summary>
    public class SystemViewModel : ViewModelBase
    {
        private readonly SystemInfoService _info;
        private readonly EventBus _bus;
        private string _status = "";

        public SystemViewModel()
        {
            _info = AppServices.SystemInfo;
            _bus = AppServices.Bus;

            RefreshCommand = new RelayCommand(_ =>
            {
                _info.SampleNow();
                _bus.Publish(EventCategory.System, "Telemetry refreshed",
                    _info.CpuPercent + " CPU \u00B7 " + _info.MemoryPercent + " memory",
                    EventSeverity.Info, "SYSTEM");
                Push();
            });

            OpenTerminalCommand = new RelayCommand(_ => Launch("cmd.exe", "", "Terminal"));
            OpenExplorerCommand = new RelayCommand(_ => Launch("explorer.exe", "", "File Explorer"));
            OpenSettingsCommand = new RelayCommand(_ => Launch("ms-settings:", "", "Windows Settings"));
            OpenTaskManagerCommand = new RelayCommand(_ => Launch("taskmgr.exe", "", "Task Manager"));
        }

        public ICommand RefreshCommand { get; }
        public ICommand OpenTerminalCommand { get; }
        public ICommand OpenExplorerCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenTaskManagerCommand { get; }

        public SystemInfoService Info => _info;
        public IEnumerable<StorageVolume> Volumes => _info.Volumes;
        public IEnumerable<double> CpuHistory => _info.CpuHistory;
        public IEnumerable<double> MemoryHistory => _info.MemoryHistory;

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        public override void OnActivated() { _info.SampleNow(); Push(); }

        public override void OnTick() { _info.Sample(); Push(); }

        private void Push()
            => OnPropertiesChanged(nameof(Info), nameof(Volumes),
                                   nameof(CpuHistory), nameof(MemoryHistory));

        private void Launch(string file, string args, string label)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = file, Arguments = args, UseShellExecute = true });
                _bus.Publish(EventCategory.System, "Local application launched", label,
                             EventSeverity.Info, "SYSTEM");
                Status = label + " launched.";
            }
            catch (Exception ex)
            {
                Status = "Could not launch " + label + ": " + ex.Message;
                _bus.Publish(EventCategory.System, "Launch failed", label + " \u00B7 " + ex.Message,
                             EventSeverity.Warning, "SYSTEM");
            }
        }
    }

    public class ServersViewModel : ViewModelBase
    {
        private readonly ServerManager _servers;
        private ManagedServer _selected;

        public ServersViewModel()
        {
            _servers = AppServices.Servers;
            _servers.Changed += Push;

            SelectCommand = new RelayCommand(p => { if (p is ManagedServer s) Selected = s; });
            StartCommand = new RelayCommand(p => { if (p is ManagedService s) _servers.StartService(s); });
            StopCommand = new RelayCommand(p => { if (p is ManagedService s) _servers.StopService(s); });
            RestartCommand = new RelayCommand(p => { if (p is ManagedService s) _servers.RestartService(s); });
            ToggleServerCommand = new RelayCommand(_ => ToggleServer(), _ => Selected != null);
            RefreshCommand = new RelayCommand(_ => _servers.RefreshMetrics());

            Selected = _servers.Servers.FirstOrDefault();
        }

        public ObservableCollection<ManagedServer> Servers => _servers.Servers;

        public ICommand SelectCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand RestartCommand { get; }
        public ICommand ToggleServerCommand { get; }
        public ICommand RefreshCommand { get; }

        public ManagedServer Selected
        {
            get => _selected;
            set { if (Set(ref _selected, value)) OnPropertyChanged(nameof(HasSelection)); }
        }

        public bool HasSelection => Selected != null;
        public int OnlineCount => _servers.OnlineCount;
        public int RunningServices => _servers.RunningServices;
        public int TotalServices => _servers.TotalServices;

        public override void OnActivated() { _servers.RefreshMetrics(); Push(); }

        public override void OnTick() => _servers.RefreshMetrics();

        public void SelectPayload(object payload)
        {
            if (payload is ManagedServer s) { Selected = s; return; }
            if (payload is ManagedService svc) Selected = _servers.ServerOf(svc);
        }

        private void ToggleServer()
        {
            if (Selected == null) return;
            _servers.SetServerStatus(Selected,
                Selected.IsOnline ? DeviceStatus.Offline : DeviceStatus.Online);
        }

        private void Push()
            => OnPropertiesChanged(nameof(OnlineCount), nameof(RunningServices), nameof(TotalServices));
    }
}

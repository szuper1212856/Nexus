using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using NEXUS.Common;
using NEXUS.Models;
using NEXUS.Services;

namespace NEXUS.ViewModels
{
    /// <summary>One clickable subsystem readout on the command console.</summary>
    public class SubsystemTile : ObservableObject
    {
        private string _value = "";
        private string _detail = "";
        private string _brushKey = "BrushCompleted";

        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Glyph { get; set; } = "Command";
        public string SectionKey { get; set; } = "";

        public string Value
        {
            get => _value;
            set => Set(ref _value, value);
        }

        public string Detail
        {
            get => _detail;
            set => Set(ref _detail, value);
        }

        public string BrushKey
        {
            get => _brushKey;
            set => Set(ref _brushKey, value);
        }
    }

    /// <summary>
    /// The operations console. Aggregates every subsystem into one live view;
    /// each tile is a navigation target into the module it summarises.
    /// </summary>
    public class CommandCenterViewModel : ViewModelBase
    {
        private readonly ShellViewModel _shell;
        private readonly EventBus _bus;
        private readonly DeviceManager _devices;
        private readonly NetworkManager _network;
        private readonly UserManager _users;
        private readonly ServerManager _servers;
        private readonly SecurityManager _security;
        private readonly SystemInfoService _sysinfo;
        private readonly TaskService _tasks;
        private readonly AutomationEngine _automations;

        public CommandCenterViewModel(ShellViewModel shell)
        {
            _shell = shell;
            _bus = AppServices.Bus;
            _devices = AppServices.Devices;
            _network = AppServices.Network;
            _users = AppServices.Users;
            _servers = AppServices.Servers;
            _security = AppServices.Security;
            _sysinfo = AppServices.SystemInfo;
            _tasks = AppServices.Tasks;
            _automations = AppServices.Automations;

            OpenCommand = new RelayCommand(p =>
            {
                var key = p as string ?? (p as SubsystemTile)?.SectionKey;
                if (!string.IsNullOrEmpty(key)) _shell.Navigate(key);
            });

            AcknowledgeCommand = new RelayCommand(p => { if (p is Alert a) _bus.Acknowledge(a); });
            AcknowledgeAllCommand = new RelayCommand(_ => _bus.AcknowledgeAll());
            SweepCommand = new RelayCommand(_ => RunSweep());
            OpenDeviceCommand = new RelayCommand(p => { if (p is Device d) _shell.OpenDevice(d); });

            BuildTiles();

            _bus.Published += _ => RefreshFeed();
            _bus.AlertsChanged += RefreshAlerts;
            _devices.Changed += Recompute;
            _servers.Changed += Recompute;
            _users.Changed += Recompute;
            _tasks.Changed += Recompute;

            Recompute();
            RefreshFeed();
            RefreshAlerts();
        }

        public ObservableCollection<SubsystemTile> Tiles { get; } = new ObservableCollection<SubsystemTile>();
        public ObservableCollection<SystemEvent> RecentEvents { get; } = new ObservableCollection<SystemEvent>();
        public ObservableCollection<Alert> OpenAlerts { get; } = new ObservableCollection<Alert>();
        public ObservableCollection<ManagedService> ActiveOperations { get; } = new ObservableCollection<ManagedService>();
        public ObservableCollection<Device> WatchList { get; } = new ObservableCollection<Device>();

        public ICommand OpenCommand { get; }
        public ICommand AcknowledgeCommand { get; }
        public ICommand AcknowledgeAllCommand { get; }
        public ICommand SweepCommand { get; }
        public ICommand OpenDeviceCommand { get; }

        public ShellViewModel Shell => _shell;

        // ---- headline readouts ----

        public string CoreStatus => "OPERATIONAL";
        public string OperatorName => _users.Current?.DisplayName ?? "UNIDENTIFIED";
        public string OperatorRole => _users.Current?.RoleLabel ?? "\u2014";

        public double CpuLoad => _sysinfo.CpuLoad;
        public double MemoryLoad => _sysinfo.MemoryLoad;
        public double StorageLoad => _sysinfo.StorageLoad;
        public string CpuPercent => _sysinfo.CpuPercent;
        public string MemoryPercent => _sysinfo.MemoryPercent;
        public string StoragePercent => _sysinfo.StoragePercent;
        public string CpuBrushKey => _sysinfo.CpuBrushKey;
        public string MemoryBrushKey => _sysinfo.MemoryBrushKey;
        public string HostName => _sysinfo.MachineName;
        public string UptimeDisplay => _sysinfo.UptimeDisplay;

        public bool HasAlerts => OpenAlerts.Count > 0;
        public int AlertCount => OpenAlerts.Count;

        // ---- connected environment ----

        public int OnlineDeviceCount => _devices.OnlineCount;
        public int OnlineServerCount => _servers.OnlineCount;
        public int RunningServiceCount => _servers.RunningServices;
        public int NetworkCount => _network.IsConnected ? 1 : 0;
        public string EnvironmentStatus => _network.IsConnected ? "ONLINE" : "DEGRADED";
        public string EnvironmentBrushKey => _network.IsConnected ? "BrushCompleted" : "BrushCritical";
        public string LatencyDisplay => _network.LatencyDisplay;
        public string LatencyBrushKey => _network.LatencyBrushKey;
        public string ThroughputDisplay => _network.ThroughputDisplay;

        // ---- real vs simulated ----

        public int RealDeviceCount => _devices.Total;
        public int RealServiceCount => _servers.TotalServices;
        public int SimEmployeeCount => AppServices.Simulation.EmployeeCount;
        public int SimWorkstationCount => AppServices.Simulation.WorkstationCount;
        public int SimOpenTickets => AppServices.Simulation.OpenTickets;

        /// <summary>The devices shown as live cards in the connected-environment strip.</summary>
        public ObservableCollection<Device> Environment { get; } = new ObservableCollection<Device>();

        public override void OnActivated()
        {
            Recompute();
            RefreshFeed();
            RefreshAlerts();
        }

        public override void OnTick()
        {
            Recompute();
            OnPropertiesChanged(nameof(CpuLoad), nameof(MemoryLoad), nameof(StorageLoad),
                nameof(CpuPercent), nameof(MemoryPercent), nameof(StoragePercent),
                nameof(CpuBrushKey), nameof(MemoryBrushKey), nameof(UptimeDisplay));
        }

        private void BuildTiles()
        {
            void Add(string key, string label, string glyph, string section)
                => Tiles.Add(new SubsystemTile { Key = key, Label = label, Glyph = glyph, SectionKey = section });

            Add("system", "SYSTEM STATUS", "System", "SYSTEM");
            Add("network", "NETWORK", "Network", "NETWORK");
            Add("devices", "DEVICES", "Devices", "DEVICES");
            Add("servers", "SERVERS", "Server", "SERVERS");
            Add("services", "SERVICES", "Automation", "SERVERS");
            Add("security", "SECURITY", "Security", "SECURITY");
            Add("users", "ACTIVE USERS", "Users", "USERS");
            Add("alerts", "ALERTS", "Alert", "SECURITY");
            Add("automations", "AUTOMATIONS", "Automation", "AUTOMATIONS");
            Add("tasks", "TASKS", "Tasks", "TASKS");
            Add("servicedesk", "SERVICE DESK", "Tasks", "SERVICEDESK");
            Add("company", "COMPANY", "Users", "DIRECTORY");
            Add("documents", "DOCUMENTS", "Documents", "DOCUMENTS");
            Add("files", "FILES", "Files", "FILES");
        }

        private void Recompute()
        {
            void Set(string key, string value, string detail, string brush)
            {
                var tile = Tiles.FirstOrDefault(t => t.Key == key);
                if (tile == null) return;
                tile.Value = value;
                tile.Detail = detail;
                tile.BrushKey = brush;
            }

            Set("system", "OPERATIONAL",
                _sysinfo.CpuPercent + " CPU \u00B7 " + _sysinfo.MemoryPercent + " RAM \u00B7 "
                + _sysinfo.StoragePercent + " DISK", "BrushCompleted");

            Set("network", _network.StatusLabel,
                _network.LatencyDisplay + " \u00B7 " + _network.PrimaryAddress, _network.StatusBrushKey);

            Set("devices", _devices.OnlineCount + " ONLINE",
                _devices.OfflineCount + " offline \u00B7 " + _devices.Total + " registered",
                _devices.OfflineCount > 0 ? "BrushHigh" : "BrushCompleted");

            Set("servers", _servers.OnlineCount + " ONLINE",
                _servers.Servers.Count + " registered",
                _servers.OnlineCount == _servers.Servers.Count ? "BrushCompleted" : "BrushCritical");

            Set("services", _servers.RunningServices + " RUNNING",
                (_servers.TotalServices - _servers.RunningServices) + " stopped",
                _servers.RunningServices == _servers.TotalServices ? "BrushCompleted" : "BrushHigh");

            Set("security", _security.PostureLabel, _security.PostureDetail, _security.PostureBrushKey);

            Set("users", _users.ActiveCount.ToString(),
                _users.SessionCount + " sessions \u00B7 " + _users.SuspendedCount + " suspended",
                _users.SuspendedCount > 0 ? "BrushHigh" : "BrushCompleted");

            Set("alerts", _bus.UnacknowledgedAlerts.ToString(),
                _bus.UnacknowledgedAlerts == 0 ? "Nothing requires attention" : "Awaiting acknowledgement",
                _bus.UnacknowledgedAlerts > 0 ? "BrushCritical" : "BrushCompleted");

            Set("automations", _automations.EnabledCount + " ARMED",
                _automations.TotalFired + " triggers fired", "BrushLow");

            var sim = AppServices.Simulation;

            Set("servicedesk", sim.OpenTickets + " OPEN",
                sim.HighPriorityTickets + " high \u00B7 " + sim.CriticalTickets + " critical \u00B7 "
                + sim.UnassignedTickets + " unassigned",
                sim.CriticalTickets > 0 ? "BrushCritical"
                    : sim.HighPriorityTickets > 0 ? "BrushHigh" : "BrushCompleted");

            Set("company", sim.EmployeeCount + " STAFF",
                sim.OnlineEmployees + " online \u00B7 " + sim.LockedAccounts + " locked \u00B7 "
                + sim.WorkstationCount + " workstations",
                sim.LockedAccounts > 0 || sim.DisabledAccounts > 0 ? "BrushHigh" : "BrushCompleted");

            Set("documents", AppServices.Files.DocumentCount + " DOCS",
                AppServices.Files.ActiveCount + " files \u00B7 " + AppServices.Files.TotalSizeDisplay,
                "BrushLow");

            Set("files", AppServices.Files.ActiveCount + " FILES",
                AppServices.Files.PinnedCount + " pinned \u00B7 " + AppServices.Files.TrashCount + " in trash",
                "BrushLow");

            Set("tasks", _tasks.Remaining + " OPEN",
                _tasks.CompletionPercent + "% complete \u00B7 " + _tasks.Overdue + " overdue",
                _tasks.Overdue > 0 ? "BrushHigh" : "BrushNormal");

            ActiveOperations.Clear();
            foreach (var s in _servers.Servers.SelectMany(x => x.Services).Where(x => x.IsRunning).Take(6))
                ActiveOperations.Add(s);

            Environment.Clear();
            foreach (var d in _devices.Devices
                     .OrderByDescending(d => d.IsOnline)
                     .ThenBy(d => d.Name).Take(8))
                Environment.Add(d);

            WatchList.Clear();
            foreach (var d in _devices.Devices
                     .OrderBy(d => d.Status == DeviceStatus.Online ? 1 : 0)
                     .ThenBy(d => d.Name).Take(7))
                WatchList.Add(d);

            OnPropertiesChanged(nameof(OperatorName), nameof(OperatorRole), nameof(HostName),
                nameof(OnlineDeviceCount), nameof(OnlineServerCount), nameof(RunningServiceCount),
                nameof(NetworkCount), nameof(EnvironmentStatus), nameof(EnvironmentBrushKey),
                nameof(LatencyDisplay), nameof(LatencyBrushKey), nameof(ThroughputDisplay),
                nameof(RealDeviceCount), nameof(RealServiceCount), nameof(SimEmployeeCount),
                nameof(SimWorkstationCount), nameof(SimOpenTickets));
        }

        private void RefreshFeed()
        {
            RecentEvents.Clear();
            foreach (var e in _bus.Events.Take(9)) RecentEvents.Add(e);
        }

        private void RefreshAlerts()
        {
            OpenAlerts.Clear();
            foreach (var a in _bus.Alerts.Where(a => !a.Acknowledged).Take(6)) OpenAlerts.Add(a);
            OnPropertiesChanged(nameof(HasAlerts), nameof(AlertCount));
            Recompute();
        }

        /// <summary>Operator-initiated pass over every subsystem, writing one audit entry per stage.</summary>
        private void RunSweep()
        {
            _sysinfo.Sample();
            _network.Refresh(quiet: false);
            _devices.RunHealthCheck();
            _servers.RefreshMetrics();

            _bus.Publish(EventCategory.System, "System check complete",
                _devices.OnlineCount + " devices online \u00B7 " + _servers.RunningServices + " services running \u00B7 security "
                + _security.PostureLabel, EventSeverity.Notice, "CORE");

            AppServices.Toasts.Show("SYSTEM CHECK COMPLETE",
                "All subsystems polled.", ToastKind.Success);

            Recompute();
        }
    }
}

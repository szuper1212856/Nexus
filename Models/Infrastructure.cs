using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using NEXUS.Common;

namespace NEXUS.Models
{
    public enum DeviceType
    {
        Workstation = 0,
        Server = 1,
        Television = 2,
        Router = 3,
        Phone = 4,
        Laptop = 5,
        Printer = 6,
        Sensor = 7,
        Unknown = 8
    }

    public enum DeviceStatus
    {
        Online = 0,
        Offline = 1,
        Degraded = 2,
        Unknown = 3
    }

    public enum LinkKind
    {
        Ethernet = 0,
        WiFi = 1,
        Loopback = 2,
        Bluetooth = 3,
        Unknown = 4
    }

    /// <summary>
    /// How a device is actually reached. NEXUS never claims to control hardware it
    /// has not been wired up to: anything other than <see cref="Connected"/> is
    /// surfaced in the UI so a simulated device can't be mistaken for a live one.
    /// </summary>
    public enum IntegrationState
    {
        /// <summary>No integration configured. Controls are inert placeholders.</summary>
        NotConfigured = 0,
        /// <summary>Modelled inside NEXUS only. Commands change NEXUS state, nothing else.</summary>
        Simulated = 1,
        /// <summary>An integration exists but has not been authorised or paired yet.</summary>
        Unlinked = 2,
        /// <summary>A real integration is live and commands reach the device.</summary>
        Connected = 3,
        /// <summary>Observed on the local network but not controllable.</summary>
        Observed = 4
    }

    /// <summary>A single control exposed by a device (power, volume, input, ...).</summary>
    public class DeviceCommand
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        public string Glyph { get; set; } = "More";
        public bool IsPrimary { get; set; }

        /// <summary>False when the bound integration cannot actually perform this control.</summary>
        public bool IsSupported { get; set; } = true;

        public string UnavailableNote =>
            "This integration does not currently expose this control.";
    }

    public class Device : ObservableObject
    {
        private string _name = "";
        private DeviceStatus _status = DeviceStatus.Unknown;
        private string _ipAddress = "";
        private DateTime? _lastSeenUtc;
        private IntegrationState _integration = IntegrationState.Simulated;
        private bool _isPowered = true;
        private int _volume = 30;
        private bool _isMuted;
        private string _currentInput = "HDMI 1";
        private string _lastCommand = "";
        private string _lastResponse = "";
        private DateTime? _lastResponseUtc;
        private bool _lastCommandSucceeded = true;

        public Guid Id { get; set; } = Guid.NewGuid();
        public DeviceType Type { get; set; } = DeviceType.Unknown;
        public LinkKind Link { get; set; } = LinkKind.WiFi;
        public string Vendor { get; set; } = "";
        public string Model { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public string IntegrationId { get; set; } = "simulated";
        public string ParentName { get; set; } = "";
        public bool IsDiscovered { get; set; }

        public string Name
        {
            get => _name;
            set => Set(ref _name, value);
        }

        public string IpAddress
        {
            get => _ipAddress;
            set => Set(ref _ipAddress, value);
        }

        public DeviceStatus Status
        {
            get => _status;
            set
            {
                if (Set(ref _status, value))
                    OnPropertiesChanged(nameof(StatusLabel), nameof(IsOnline));
            }
        }

        public DateTime? LastSeenUtc
        {
            get => _lastSeenUtc;
            set { if (Set(ref _lastSeenUtc, value)) OnPropertyChanged(nameof(LastSeenDisplay)); }
        }

        public IntegrationState Integration
        {
            get => _integration;
            set
            {
                if (Set(ref _integration, value))
                    OnPropertiesChanged(nameof(IntegrationLabel), nameof(IsLive), nameof(IntegrationNotice));
            }
        }

        // ---- soft state, meaningful for simulated and (later) real devices ----

        public bool IsPowered
        {
            get => _isPowered;
            set { if (Set(ref _isPowered, value)) OnPropertyChanged(nameof(PowerLabel)); }
        }

        public int Volume
        {
            get => _volume;
            set => Set(ref _volume, value < 0 ? 0 : (value > 100 ? 100 : value));
        }

        public bool IsMuted
        {
            get => _isMuted;
            set => Set(ref _isMuted, value);
        }

        public string CurrentInput
        {
            get => _currentInput;
            set => Set(ref _currentInput, value);
        }

        /// <summary>Most recent control sent from this console, for the status block.</summary>
        public string LastCommand
        {
            get => _lastCommand;
            set { if (Set(ref _lastCommand, value)) OnPropertyChanged(nameof(LastCommandDisplay)); }
        }

        public string LastResponse
        {
            get => _lastResponse;
            set => Set(ref _lastResponse, value);
        }

        public DateTime? LastResponseUtc
        {
            get => _lastResponseUtc;
            set { if (Set(ref _lastResponseUtc, value)) OnPropertyChanged(nameof(LastResponseDisplay)); }
        }

        public bool LastCommandSucceeded
        {
            get => _lastCommandSucceeded;
            set { if (Set(ref _lastCommandSucceeded, value)) OnPropertyChanged(nameof(ResponseBrushKey)); }
        }

        [JsonIgnore] public string LastCommandDisplay =>
            string.IsNullOrEmpty(LastCommand) ? "NONE THIS SESSION" : LastCommand.ToUpperInvariant();

        [JsonIgnore] public string LastResponseDisplay =>
            LastResponseUtc.HasValue ? LastResponseUtc.Value.ToLocalTime().ToString("HH:mm:ss") : "\u2014";

        [JsonIgnore] public string ResponseBrushKey =>
            string.IsNullOrEmpty(LastResponse) ? "BrushTextMuted"
            : LastCommandSucceeded ? "BrushCompleted" : "BrushCritical";

        /// <summary>How NEXUS reaches this device, shown in the connection row.</summary>
        [JsonIgnore]
        public string ConnectionLabel => Integration switch
        {
            IntegrationState.Connected => "LIVE \u00B7 " + LinkLabel,
            IntegrationState.Simulated => "NEXUS RECORD \u00B7 " + LinkLabel,
            IntegrationState.Observed => "OBSERVED \u00B7 " + LinkLabel,
            IntegrationState.Unlinked => "NOT PAIRED",
            _ => "NO INTEGRATION"
        };

        // ---- display ----

        [JsonIgnore] public bool IsOnline => Status == DeviceStatus.Online;
        [JsonIgnore] public bool IsLive => Integration == IntegrationState.Connected;
        [JsonIgnore] public string PowerLabel => IsPowered ? "POWERED" : "STANDBY";

        [JsonIgnore]
        public string StatusLabel => Status switch
        {
            DeviceStatus.Online => "ONLINE",
            DeviceStatus.Offline => "OFFLINE",
            DeviceStatus.Degraded => "DEGRADED",
            _ => "UNKNOWN"
        };

        [JsonIgnore]
        public string StatusBrushKey => Status switch
        {
            DeviceStatus.Online => "BrushCompleted",
            DeviceStatus.Offline => "BrushCritical",
            DeviceStatus.Degraded => "BrushHigh",
            _ => "BrushTextMuted"
        };

        [JsonIgnore]
        public string TypeLabel => Type switch
        {
            DeviceType.Workstation => "WORKSTATION",
            DeviceType.Server => "SERVER",
            DeviceType.Television => "SMART TV",
            DeviceType.Router => "ROUTER",
            DeviceType.Phone => "PHONE",
            DeviceType.Laptop => "LAPTOP",
            DeviceType.Printer => "PRINTER",
            DeviceType.Sensor => "SENSOR",
            _ => "UNKNOWN"
        };

        /// <summary>Icon name resolved through the central registry, never a raw codepoint.</summary>
        [JsonIgnore]
        public string Glyph => Type switch
        {
            DeviceType.Workstation => "Workstation",
            DeviceType.Server => "Server",
            DeviceType.Television => "Television",
            DeviceType.Router => "Router",
            DeviceType.Phone => "Phone",
            DeviceType.Laptop => "Laptop",
            DeviceType.Printer => "Printer",
            DeviceType.Sensor => "Sensor",
            _ => "UnknownNode"
        };

        [JsonIgnore] public string LinkLabel => Link switch
        {
            LinkKind.Ethernet => "ETHERNET",
            LinkKind.WiFi => "WI-FI",
            LinkKind.Loopback => "LOCAL",
            LinkKind.Bluetooth => "BLUETOOTH",
            _ => "UNKNOWN"
        };

        [JsonIgnore]
        public string IntegrationLabel => Integration switch
        {
            IntegrationState.Connected => "LIVE INTEGRATION",
            IntegrationState.Simulated => "SIMULATED",
            IntegrationState.Unlinked => "NOT PAIRED",
            IntegrationState.Observed => "OBSERVED ONLY",
            _ => "NO INTEGRATION"
        };

        /// <summary>Plain-language warning shown on the device page so nothing is misleading.</summary>
        [JsonIgnore]
        public string IntegrationNotice => Integration switch
        {
            IntegrationState.Connected => "",
            IntegrationState.Simulated =>
                "Modelled inside NEXUS. Controls update this record only \u2014 no physical device is contacted.",
            IntegrationState.Unlinked =>
                "An integration exists for this device type but has not been paired or authorised.",
            IntegrationState.Observed =>
                "Seen on the local network. NEXUS can report on it but cannot control it.",
            _ => "No integration is configured for this device. Controls are inactive."
        };

        [JsonIgnore]
        public string IntegrationBrushKey => Integration switch
        {
            IntegrationState.Connected => "BrushCompleted",
            IntegrationState.Simulated => "BrushLow",
            IntegrationState.Observed => "BrushNormal",
            _ => "BrushTextMuted"
        };

        [JsonIgnore]
        public string LastSeenDisplay
        {
            get
            {
                if (!LastSeenUtc.HasValue) return "NEVER";
                var delta = DateTime.UtcNow - LastSeenUtc.Value;
                if (delta.TotalSeconds < 45) return "NOW";
                if (delta.TotalMinutes < 60) return (int)delta.TotalMinutes + " MIN AGO";
                if (delta.TotalHours < 24) return (int)delta.TotalHours + " HR AGO";
                return LastSeenUtc.Value.ToLocalTime().ToString("dd MMM HH:mm").ToUpperInvariant();
            }
        }

        [JsonIgnore] public string AddressDisplay => string.IsNullOrWhiteSpace(IpAddress) ? "\u2014" : IpAddress;

        public void RefreshTimeDependent() => OnPropertyChanged(nameof(LastSeenDisplay));

        /// <summary>Controls offered for this device type. Availability depends on the integration state.</summary>
        [JsonIgnore]
        public ObservableCollection<DeviceCommand> Controls
        {
            get
            {
                var list = new ObservableCollection<DeviceCommand>();
                void Add(string key, string label, string glyph, bool primary = false)
                    => list.Add(new DeviceCommand { Key = key, Label = label, Glyph = glyph, IsPrimary = primary });

                switch (Type)
                {
                    case DeviceType.Television:
                        Add("power", "POWER", "Power", true);
                        Add("vol-down", "VOLUME \u2212", "VolumeDown");
                        Add("vol-up", "VOLUME +", "VolumeUp");
                        Add("mute", "MUTE", "Mute");
                        Add("home", "HOME", "Home");
                        Add("back", "BACK", "Back");
                        Add("input", "INPUT", "Input");
                        break;

                    case DeviceType.Server:
                        Add("power", "POWER", "Power", true);
                        Add("restart", "RESTART", "Restart");
                        Add("logs", "LOGS", "Log");
                        Add("ping", "PING", "Ping");
                        break;

                    case DeviceType.Router:
                        Add("restart", "REBOOT", "Restart", true);
                        Add("ping", "PING", "Ping");
                        Add("clients", "CLIENTS", "Clients");
                        break;

                    case DeviceType.Workstation:
                    case DeviceType.Laptop:
                        Add("power", "POWER", "Power", true);
                        Add("lock", "LOCK", "Lock");
                        Add("ping", "PING", "Ping");
                        break;

                    default:
                        Add("power", "POWER", "Power", true);
                        Add("ping", "PING", "Ping");
                        break;
                }

                return list;
            }
        }
    }

    /// <summary>A network interface belonging to the machine NEXUS runs on.</summary>
    public class NetworkInterfaceInfo
    {
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public string MacAddress { get; set; } = "";
        public string Speed { get; set; } = "";
        public bool IsUp { get; set; }
        public LinkKind Link { get; set; } = LinkKind.Unknown;

        public string StatusLabel => IsUp ? "UP" : "DOWN";
        public string StatusBrushKey => IsUp ? "BrushCompleted" : "BrushTextMuted";
        public string LinkLabel => Link == LinkKind.Ethernet ? "ETHERNET"
            : Link == LinkKind.WiFi ? "WI-FI"
            : Link == LinkKind.Loopback ? "LOOPBACK" : "OTHER";
    }
}

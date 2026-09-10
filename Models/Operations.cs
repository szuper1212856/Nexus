using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using NEXUS.Common;

namespace NEXUS.Models
{
    // ==================== EVENTS ====================

    public enum EventCategory
    {
        System = 0,
        Auth = 1,
        Access = 2,
        Device = 3,
        Network = 4,
        Server = 5,
        Service = 6,
        Security = 7,
        Automation = 8,
        Task = 9,
        Terminal = 10,
        File = 11,
        Ticket = 12,
        Simulation = 13
    }

    public enum EventSeverity
    {
        Info = 0,
        Notice = 1,
        Warning = 2,
        Critical = 3
    }

    /// <summary>
    /// One entry in the NEXUS audit trail. Every event is raised by real application
    /// activity; nothing here is invented to pad the log.
    /// </summary>
    public class SystemEvent
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public EventCategory Category { get; set; }
        public EventSeverity Severity { get; set; }
        public string Message { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Actor { get; set; } = "";
        public string Source { get; set; } = "";

        [JsonIgnore] public DateTime Timestamp => TimestampUtc.ToLocalTime();
        [JsonIgnore] public string TimeDisplay => Timestamp.ToString("HH:mm:ss");
        [JsonIgnore] public string DateDisplay => Timestamp.ToString("dd MMM").ToUpperInvariant();
        [JsonIgnore] public string CategoryLabel => Category.ToString().ToUpperInvariant();

        [JsonIgnore]
        public string CategoryBrushKey => Category switch
        {
            EventCategory.Auth => "BrushAccent",
            EventCategory.Access => "BrushHigh",
            EventCategory.Device => "BrushLow",
            EventCategory.Network => "BrushLow",
            EventCategory.Server => "BrushNormal",
            EventCategory.Service => "BrushNormal",
            EventCategory.Security => "BrushCritical",
            EventCategory.Automation => "BrushCompleted",
            EventCategory.Task => "BrushCompleted",
            EventCategory.Terminal => "BrushPlanned",
            EventCategory.File => "BrushLow",
            EventCategory.Ticket => "BrushHigh",
            EventCategory.Simulation => "BrushAccent",
            _ => "BrushTextMuted"
        };

        [JsonIgnore]
        public string SeverityBrushKey => Severity switch
        {
            EventSeverity.Critical => "BrushCritical",
            EventSeverity.Warning => "BrushHigh",
            EventSeverity.Notice => "BrushNormal",
            _ => "BrushTextMuted"
        };

        [JsonIgnore] public string SeverityLabel => Severity.ToString().ToUpperInvariant();
    }

    public class Alert : ObservableObject
    {
        private bool _acknowledged;

        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime RaisedUtc { get; set; } = DateTime.UtcNow;
        public EventSeverity Severity { get; set; } = EventSeverity.Warning;
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Source { get; set; } = "";

        public bool Acknowledged
        {
            get => _acknowledged;
            set => Set(ref _acknowledged, value);
        }

        [JsonIgnore] public string TimeDisplay => RaisedUtc.ToLocalTime().ToString("HH:mm:ss");
        [JsonIgnore] public string SeverityLabel => Severity.ToString().ToUpperInvariant();

        [JsonIgnore]
        public string SeverityBrushKey => Severity switch
        {
            EventSeverity.Critical => "BrushCritical",
            EventSeverity.Warning => "BrushHigh",
            EventSeverity.Notice => "BrushNormal",
            _ => "BrushLow"
        };
    }

    // ==================== SERVERS & SERVICES ====================

    public enum ServiceState
    {
        Running = 0,
        Stopped = 1,
        Starting = 2,
        Faulted = 3
    }

    public class ManagedService : ObservableObject
    {
        private ServiceState _state = ServiceState.Stopped;
        private DateTime? _startedUtc;

        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public int Port { get; set; }

        /// <summary>Identifier of the integration that would back this service, if any.</summary>
        public string IntegrationId { get; set; } = "simulated";
        public IntegrationState Integration { get; set; } = IntegrationState.Simulated;

        public ServiceState State
        {
            get => _state;
            set
            {
                if (Set(ref _state, value))
                    OnPropertiesChanged(nameof(StateLabel), nameof(StateBrushKey), nameof(IsRunning));
            }
        }

        public DateTime? StartedUtc
        {
            get => _startedUtc;
            set { if (Set(ref _startedUtc, value)) OnPropertyChanged(nameof(UptimeDisplay)); }
        }

        [JsonIgnore] public bool IsRunning => State == ServiceState.Running;

        [JsonIgnore]
        public string StateLabel => State switch
        {
            ServiceState.Running => "RUNNING",
            ServiceState.Starting => "STARTING",
            ServiceState.Faulted => "FAULTED",
            _ => "STOPPED"
        };

        [JsonIgnore]
        public string StateBrushKey => State switch
        {
            ServiceState.Running => "BrushCompleted",
            ServiceState.Starting => "BrushNormal",
            ServiceState.Faulted => "BrushCritical",
            _ => "BrushTextMuted"
        };

        [JsonIgnore] public string PortDisplay => Port > 0 ? ":" + Port : "\u2014";

        [JsonIgnore]
        public string UptimeDisplay
        {
            get
            {
                if (!IsRunning || !StartedUtc.HasValue) return "\u2014";
                var d = DateTime.UtcNow - StartedUtc.Value;
                if (d.TotalMinutes < 1) return "<1M";
                if (d.TotalHours < 1) return (int)d.TotalMinutes + "M";
                if (d.TotalDays < 1) return (int)d.TotalHours + "H " + d.Minutes + "M";
                return (int)d.TotalDays + "D " + d.Hours + "H";
            }
        }

        public void RefreshTimeDependent() => OnPropertyChanged(nameof(UptimeDisplay));
    }

    public class ManagedServer : ObservableObject
    {
        private DeviceStatus _status = DeviceStatus.Online;
        private double _cpuLoad;
        private double _memoryLoad;
        private double _storageLoad;
        private DateTime? _bootedUtc;

        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public string Host { get; set; } = "";
        public string OperatingSystem { get; set; } = "";
        public string IntegrationId { get; set; } = "simulated";
        public IntegrationState Integration { get; set; } = IntegrationState.Simulated;

        /// <summary>Set for the server record that represents this machine; its metrics are real.</summary>
        public bool IsLocalMachine { get; set; }

        public ObservableCollection<ManagedService> Services { get; set; } = new ObservableCollection<ManagedService>();

        public DeviceStatus Status
        {
            get => _status;
            set
            {
                if (Set(ref _status, value))
                    OnPropertiesChanged(nameof(StatusLabel), nameof(StatusBrushKey), nameof(IsOnline));
            }
        }

        public double CpuLoad
        {
            get => _cpuLoad;
            set { if (Set(ref _cpuLoad, value)) OnPropertyChanged(nameof(CpuPercent)); }
        }

        public double MemoryLoad
        {
            get => _memoryLoad;
            set { if (Set(ref _memoryLoad, value)) OnPropertyChanged(nameof(MemoryPercent)); }
        }

        public double StorageLoad
        {
            get => _storageLoad;
            set { if (Set(ref _storageLoad, value)) OnPropertyChanged(nameof(StoragePercent)); }
        }

        public DateTime? BootedUtc
        {
            get => _bootedUtc;
            set { if (Set(ref _bootedUtc, value)) OnPropertyChanged(nameof(UptimeDisplay)); }
        }

        [JsonIgnore] public bool IsOnline => Status == DeviceStatus.Online;
        [JsonIgnore] public string CpuPercent => (int)Math.Round(CpuLoad * 100) + "%";
        [JsonIgnore] public string MemoryPercent => (int)Math.Round(MemoryLoad * 100) + "%";
        [JsonIgnore] public string StoragePercent => (int)Math.Round(StorageLoad * 100) + "%";

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
        public string UptimeDisplay
        {
            get
            {
                if (!BootedUtc.HasValue || !IsOnline) return "\u2014";
                var d = DateTime.UtcNow - BootedUtc.Value;
                if (d.TotalHours < 1) return (int)d.TotalMinutes + "M";
                if (d.TotalDays < 1) return (int)d.TotalHours + "H " + d.Minutes + "M";
                return (int)d.TotalDays + "D " + d.Hours + "H";
            }
        }

        [JsonIgnore]
        public int RunningServices
        {
            get
            {
                var n = 0;
                foreach (var s in Services) if (s.IsRunning) n++;
                return n;
            }
        }

        [JsonIgnore] public string ServiceSummary => RunningServices + "/" + Services.Count + " RUNNING";

        [JsonIgnore]
        public string IntegrationLabel => IsLocalMachine ? "LOCAL MACHINE \u00B7 LIVE METRICS"
            : Integration == IntegrationState.Connected ? "LIVE INTEGRATION" : "SIMULATED";

        public void Refresh()
            => OnPropertiesChanged(nameof(UptimeDisplay), nameof(RunningServices), nameof(ServiceSummary),
                                   nameof(CpuPercent), nameof(MemoryPercent), nameof(StoragePercent));
    }

    // ==================== AUTOMATIONS ====================

    public enum TriggerType
    {
        DeviceOnline = 0,
        DeviceOffline = 1,
        ServerOffline = 2,
        ServiceStopped = 3,
        UserSuspended = 4,
        UserAuthenticated = 5,
        TaskCompleted = 6,
        DeadlineWithin24h = 7,
        PermissionChanged = 8,
        SecurityEvent = 9,
        FileUploaded = 10,
        DocumentSaved = 11
    }

    public enum AutomationActionType
    {
        RaiseAlert = 0,
        LogEvent = 1,
        Notify = 2,
        RevokeSessions = 3,
        MarkDeviceOffline = 4,
        RestartService = 5,
        CreateTask = 6,
        PinFile = 7
    }

    public class AutomationRule : ObservableObject
    {
        private bool _isEnabled = true;
        private int _timesFired;
        private DateTime? _lastFiredUtc;

        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public TriggerType Trigger { get; set; }
        public AutomationActionType Action { get; set; }

        /// <summary>Optional filter on the event subject, e.g. a device or service name. Empty = any.</summary>
        public string Condition { get; set; } = "";
        public string ActionParameter { get; set; } = "";
        public bool IsBuiltIn { get; set; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set { if (Set(ref _isEnabled, value)) OnPropertyChanged(nameof(StateLabel)); }
        }

        public int TimesFired
        {
            get => _timesFired;
            set => Set(ref _timesFired, value);
        }

        public DateTime? LastFiredUtc
        {
            get => _lastFiredUtc;
            set { if (Set(ref _lastFiredUtc, value)) OnPropertyChanged(nameof(LastFiredDisplay)); }
        }

        [JsonIgnore] public string StateLabel => IsEnabled ? "ARMED" : "DISABLED";
        [JsonIgnore] public string StateBrushKey => IsEnabled ? "BrushCompleted" : "BrushTextMuted";

        [JsonIgnore]
        public string TriggerLabel => Trigger switch
        {
            TriggerType.DeviceOnline => "DEVICE COMES ONLINE",
            TriggerType.DeviceOffline => "DEVICE GOES OFFLINE",
            TriggerType.ServerOffline => "SERVER GOES OFFLINE",
            TriggerType.ServiceStopped => "SERVICE STOPS",
            TriggerType.UserSuspended => "USER IS SUSPENDED",
            TriggerType.UserAuthenticated => "USER AUTHENTICATES",
            TriggerType.TaskCompleted => "TASK IS COMPLETED",
            TriggerType.DeadlineWithin24h => "DEADLINE WITHIN 24 HOURS",
            TriggerType.PermissionChanged => "PERMISSION IS CHANGED",
            TriggerType.FileUploaded => "A FILE IS UPLOADED",
            TriggerType.DocumentSaved => "A DOCUMENT IS SAVED",
            _ => "SECURITY EVENT OCCURS"
        };

        [JsonIgnore]
        public string ActionLabel => Action switch
        {
            AutomationActionType.RaiseAlert => "RAISE AN ALERT",
            AutomationActionType.LogEvent => "WRITE AN AUDIT ENTRY",
            AutomationActionType.Notify => "SHOW A NOTIFICATION",
            AutomationActionType.RevokeSessions => "REVOKE ACTIVE SESSIONS",
            AutomationActionType.MarkDeviceOffline => "MARK THE DEVICE OFFLINE",
            AutomationActionType.RestartService => "RESTART THE SERVICE",
            AutomationActionType.PinFile => "PIN THE FILE",
            _ => "CREATE A TASK"
        };

        [JsonIgnore]
        public string ConditionLabel => string.IsNullOrWhiteSpace(Condition)
            ? "ANY SUBJECT"
            : "SUBJECT MATCHES \"" + Condition.ToUpperInvariant() + "\"";

        [JsonIgnore]
        public string LastFiredDisplay => LastFiredUtc.HasValue
            ? LastFiredUtc.Value.ToLocalTime().ToString("dd MMM HH:mm").ToUpperInvariant()
            : "NEVER";
    }

    /// <summary>Payload handed to the automation engine whenever something happens.</summary>
    public class NexusSignal
    {
        public TriggerType Trigger { get; set; }
        public string Subject { get; set; } = "";
        public string Detail { get; set; } = "";
        public object Payload { get; set; }
    }
}

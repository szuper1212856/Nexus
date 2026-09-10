using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using NEXUS.Common;

namespace NEXUS.Models
{
    // ==================== ORGANISATION ====================

    public enum Department
    {
        IT = 0,
        Engineering = 1,
        Finance = 2,
        HR = 3,
        Marketing = 4,
        Sales = 5,
        Management = 6,
        Operations = 7
    }

    public enum AccountState
    {
        Active = 0,
        Disabled = 1,
        Locked = 2,
        PasswordExpired = 3
    }

    /// <summary>How an employee behaves in ticket conversations.</summary>
    public enum Temperament
    {
        Patient = 0,
        Terse = 1,
        Anxious = 2,
        Technical = 3,
        Frustrated = 4
    }

    public class Employee : ObservableObject
    {
        private AccountState _accountState = AccountState.Active;
        private Department _department;
        private bool _isOnline = true;
        private bool _mfaEnabled = true;
        private DateTime? _lastActivityUtc = DateTime.UtcNow;
        private string _jobTitle = "";

        public Guid Id { get; set; } = Guid.NewGuid();
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Username { get; set; } = "";
        public Temperament Temperament { get; set; } = Temperament.Patient;
        public DateTime HiredUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Asset tag of the workstation assigned to this employee.</summary>
        public string WorkstationTag { get; set; } = "";

        public ObservableCollection<string> Applications { get; set; } = new ObservableCollection<string>();
        public ObservableCollection<string> Groups { get; set; } = new ObservableCollection<string>();

        /// <summary>Simulated password lifecycle only. No credential is ever stored.</summary>
        public DateTime PasswordSetUtc { get; set; } = DateTime.UtcNow.AddDays(-40);
        public int FailedSignIns { get; set; }

        public Department Department
        {
            get => _department;
            set { if (Set(ref _department, value)) OnPropertiesChanged(nameof(DepartmentLabel), nameof(Email)); }
        }

        public string JobTitle
        {
            get => _jobTitle;
            set => Set(ref _jobTitle, value);
        }

        public AccountState AccountState
        {
            get => _accountState;
            set
            {
                if (Set(ref _accountState, value))
                    OnPropertiesChanged(nameof(AccountLabel), nameof(AccountBrushKey), nameof(CanSignIn),
                                        nameof(PasswordStatusLabel));
            }
        }

        public bool IsOnline
        {
            get => _isOnline;
            set { if (Set(ref _isOnline, value)) OnPropertiesChanged(nameof(PresenceLabel), nameof(PresenceBrushKey)); }
        }

        public bool MfaEnabled
        {
            get => _mfaEnabled;
            set { if (Set(ref _mfaEnabled, value)) OnPropertyChanged(nameof(MfaLabel)); }
        }

        public DateTime? LastActivityUtc
        {
            get => _lastActivityUtc;
            set { if (Set(ref _lastActivityUtc, value)) OnPropertyChanged(nameof(LastActivityDisplay)); }
        }

        // ---- display ----

        [JsonIgnore] public string FullName => (FirstName + " " + LastName).Trim();
        [JsonIgnore] public string Email => Username + "@nexus.local";
        [JsonIgnore] public string DepartmentLabel => Department.ToString().ToUpperInvariant();
        [JsonIgnore] public bool CanSignIn => AccountState == AccountState.Active;
        [JsonIgnore] public string MfaLabel => MfaEnabled ? "ENABLED" : "NOT ENROLLED";

        [JsonIgnore]
        public string Initials
        {
            get
            {
                var a = string.IsNullOrEmpty(FirstName) ? "?" : FirstName.Substring(0, 1);
                var b = string.IsNullOrEmpty(LastName) ? "" : LastName.Substring(0, 1);
                return (a + b).ToUpperInvariant();
            }
        }

        [JsonIgnore]
        public string AccountLabel => AccountState switch
        {
            AccountState.Active => "ACTIVE",
            AccountState.Disabled => "DISABLED",
            AccountState.Locked => "LOCKED",
            _ => "PASSWORD EXPIRED"
        };

        [JsonIgnore]
        public string AccountBrushKey => AccountState switch
        {
            AccountState.Active => "BrushCompleted",
            AccountState.Disabled => "BrushCritical",
            AccountState.Locked => "BrushHigh",
            _ => "BrushNormal"
        };

        [JsonIgnore]
        public string PasswordStatusLabel => AccountState switch
        {
            AccountState.Locked => "LOCKED",
            AccountState.PasswordExpired => "REQUIRES RESET",
            _ => (DateTime.UtcNow - PasswordSetUtc).TotalDays > 90 ? "EXPIRED" : "SET"
        };

        [JsonIgnore] public string PresenceLabel => !CanSignIn ? "SIGNED OUT" : IsOnline ? "ONLINE" : "OFFLINE";

        [JsonIgnore]
        public string PresenceBrushKey => !CanSignIn ? "BrushTextMuted"
            : IsOnline ? "BrushCompleted" : "BrushTextMuted";

        [JsonIgnore]
        public string LastActivityDisplay
        {
            get
            {
                if (!LastActivityUtc.HasValue) return "NEVER";
                var d = DateTime.UtcNow - LastActivityUtc.Value;
                if (d.TotalMinutes < 2) return "JUST NOW";
                if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " MIN AGO";
                if (d.TotalHours < 24) return (int)d.TotalHours + " HR AGO";
                return LastActivityUtc.Value.ToLocalTime().ToString("dd MMM HH:mm").ToUpperInvariant();
            }
        }

        public void RefreshTimeDependent()
            => OnPropertiesChanged(nameof(LastActivityDisplay), nameof(PasswordStatusLabel));
    }

    // ==================== SIMULATED WORKSTATIONS ====================

    public class Workstation : ObservableObject
    {
        private bool _isOnline = true;
        private double _cpuLoad;
        private double _memoryLoad;
        private double _diskUsed;

        public Guid Id { get; set; } = Guid.NewGuid();
        public string AssetTag { get; set; } = "";
        public string Model { get; set; } = "";
        public string OperatingSystem { get; set; } = "Windows 11 Pro";
        public string IpAddress { get; set; } = "";
        public string AssignedUsername { get; set; } = "";
        public Department Department { get; set; }
        public int TotalMemoryGb { get; set; } = 16;
        public int TotalDiskGb { get; set; } = 512;
        public DateTime LastBootUtc { get; set; } = DateTime.UtcNow.AddHours(-9);

        public bool IsOnline
        {
            get => _isOnline;
            set { if (Set(ref _isOnline, value)) OnPropertiesChanged(nameof(StatusLabel), nameof(StatusBrushKey)); }
        }

        public double CpuLoad
        {
            get => _cpuLoad;
            set { if (Set(ref _cpuLoad, value)) OnPropertiesChanged(nameof(CpuPercent), nameof(HealthLabel), nameof(HealthBrushKey)); }
        }

        public double MemoryLoad
        {
            get => _memoryLoad;
            set { if (Set(ref _memoryLoad, value)) OnPropertiesChanged(nameof(MemoryPercent), nameof(HealthLabel), nameof(HealthBrushKey)); }
        }

        public double DiskUsed
        {
            get => _diskUsed;
            set { if (Set(ref _diskUsed, value)) OnPropertiesChanged(nameof(DiskPercent), nameof(FreeDiskDisplay), nameof(HealthLabel), nameof(HealthBrushKey)); }
        }

        [JsonIgnore] public string StatusLabel => IsOnline ? "ONLINE" : "OFFLINE";
        [JsonIgnore] public string StatusBrushKey => IsOnline ? "BrushCompleted" : "BrushCritical";
        [JsonIgnore] public string CpuPercent => (int)Math.Round(CpuLoad * 100) + "%";
        [JsonIgnore] public string MemoryPercent => (int)Math.Round(MemoryLoad * 100) + "%";
        [JsonIgnore] public string DiskPercent => (int)Math.Round(DiskUsed * 100) + "%";

        [JsonIgnore]
        public string FreeDiskDisplay => Math.Round(TotalDiskGb * (1 - DiskUsed), 1) + " GB FREE";

        [JsonIgnore]
        public string UptimeDisplay
        {
            get
            {
                if (!IsOnline) return "\u2014";
                var d = DateTime.UtcNow - LastBootUtc;
                if (d.TotalHours < 1) return (int)d.TotalMinutes + "M";
                if (d.TotalDays < 1) return (int)d.TotalHours + "H " + d.Minutes + "M";
                return (int)d.TotalDays + "D " + d.Hours + "H";
            }
        }

        /// <summary>Worst of the three load figures decides the headline health.</summary>
        [JsonIgnore]
        public string HealthLabel
        {
            get
            {
                if (!IsOnline) return "UNREACHABLE";
                var worst = Math.Max(CpuLoad, Math.Max(MemoryLoad, DiskUsed));
                if (worst > 0.9) return "CRITICAL";
                if (worst > 0.75) return "DEGRADED";
                return "HEALTHY";
            }
        }

        [JsonIgnore]
        public string HealthBrushKey => HealthLabel switch
        {
            "CRITICAL" => "BrushCritical",
            "DEGRADED" => "BrushHigh",
            "UNREACHABLE" => "BrushTextMuted",
            _ => "BrushCompleted"
        };

        public void Refresh()
            => OnPropertiesChanged(nameof(UptimeDisplay), nameof(HealthLabel), nameof(HealthBrushKey),
                                   nameof(CpuPercent), nameof(MemoryPercent), nameof(DiskPercent));
    }

    // ==================== SERVICE DESK ====================

    public enum TicketPriority { Low = 0, Normal = 1, High = 2, Critical = 3 }

    public enum TicketStatus
    {
        Open = 0,
        InProgress = 1,
        WaitingForUser = 2,
        Resolved = 3,
        Closed = 4
    }

    public enum TicketCategory
    {
        Hardware = 0,
        Software = 1,
        Account = 2,
        Network = 3,
        Printing = 4,
        Security = 5,
        Access = 6,
        Email = 7
    }

    public enum TicketDifficulty { Easy = 0, Medium = 1, Hard = 2, Critical = 3 }

    public class TicketMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime SentUtc { get; set; } = DateTime.UtcNow;
        public string Author { get; set; } = "";
        public string Body { get; set; } = "";

        /// <summary>True when the technician (the operator) wrote it.</summary>
        public bool FromTechnician { get; set; }

        [JsonIgnore] public string TimeDisplay => SentUtc.ToLocalTime().ToString("HH:mm");
        [JsonIgnore] public string DateDisplay => SentUtc.ToLocalTime().ToString("dd MMM").ToUpperInvariant();
        [JsonIgnore] public string AlignLabel => FromTechnician ? "TECHNICIAN" : "REQUESTER";
        [JsonIgnore] public string BrushKey => FromTechnician ? "BrushAccent" : "BrushLow";
    }

    public class Ticket : ObservableObject
    {
        private TicketStatus _status = TicketStatus.Open;
        private TicketPriority _priority = TicketPriority.Normal;
        private string _assignedTo = "";
        private DateTime _updatedUtc = DateTime.UtcNow;

        public Guid Id { get; set; } = Guid.NewGuid();
        public string Reference { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string RequesterUsername { get; set; } = "";
        public Department Department { get; set; }
        public string WorkstationTag { get; set; } = "";
        public TicketCategory Category { get; set; }
        public TicketDifficulty Difficulty { get; set; } = TicketDifficulty.Easy;
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedUtc { get; set; }

        /// <summary>Short note describing the underlying cause, revealed on resolution.</summary>
        public string RootCause { get; set; } = "";

        public ObservableCollection<TicketMessage> Conversation { get; set; } =
            new ObservableCollection<TicketMessage>();

        public TicketStatus Status
        {
            get => _status;
            set
            {
                if (Set(ref _status, value))
                    OnPropertiesChanged(nameof(StatusLabel), nameof(StatusBrushKey), nameof(IsOpen), nameof(IsResolved));
            }
        }

        public TicketPriority Priority
        {
            get => _priority;
            set { if (Set(ref _priority, value)) OnPropertiesChanged(nameof(PriorityLabel), nameof(PriorityBrushKey)); }
        }

        public string AssignedTo
        {
            get => _assignedTo;
            set { if (Set(ref _assignedTo, value)) OnPropertiesChanged(nameof(AssignedLabel), nameof(IsAssigned)); }
        }

        public DateTime UpdatedUtc
        {
            get => _updatedUtc;
            set { if (Set(ref _updatedUtc, value)) OnPropertyChanged(nameof(UpdatedDisplay)); }
        }

        // ---- display ----

        [JsonIgnore] public bool IsOpen => Status != TicketStatus.Resolved && Status != TicketStatus.Closed;
        [JsonIgnore] public bool IsResolved => Status == TicketStatus.Resolved || Status == TicketStatus.Closed;
        [JsonIgnore] public bool IsAssigned => !string.IsNullOrWhiteSpace(AssignedTo);
        [JsonIgnore] public string AssignedLabel => IsAssigned ? AssignedTo.ToUpperInvariant() : "UNASSIGNED";
        [JsonIgnore] public string CategoryLabel => Category.ToString().ToUpperInvariant();
        [JsonIgnore] public string DifficultyLabel => Difficulty.ToString().ToUpperInvariant();
        [JsonIgnore] public string DepartmentLabel => Department.ToString().ToUpperInvariant();
        [JsonIgnore] public int MessageCount => Conversation.Count;

        [JsonIgnore]
        public string StatusLabel => Status switch
        {
            TicketStatus.Open => "OPEN",
            TicketStatus.InProgress => "IN PROGRESS",
            TicketStatus.WaitingForUser => "WAITING FOR USER",
            TicketStatus.Resolved => "RESOLVED",
            _ => "CLOSED"
        };

        [JsonIgnore]
        public string StatusBrushKey => Status switch
        {
            TicketStatus.Open => "BrushHigh",
            TicketStatus.InProgress => "BrushAccent",
            TicketStatus.WaitingForUser => "BrushNormal",
            TicketStatus.Resolved => "BrushCompleted",
            _ => "BrushTextMuted"
        };

        [JsonIgnore]
        public string PriorityLabel => Priority switch
        {
            TicketPriority.Critical => "CRITICAL",
            TicketPriority.High => "HIGH",
            TicketPriority.Normal => "NORMAL",
            _ => "LOW"
        };

        [JsonIgnore]
        public string PriorityBrushKey => Priority switch
        {
            TicketPriority.Critical => "BrushCritical",
            TicketPriority.High => "BrushHigh",
            TicketPriority.Normal => "BrushNormal",
            _ => "BrushLow"
        };

        [JsonIgnore]
        public string AgeDisplay
        {
            get
            {
                var d = DateTime.UtcNow - CreatedUtc;
                if (d.TotalMinutes < 60) return (int)d.TotalMinutes + "M";
                if (d.TotalDays < 1) return (int)d.TotalHours + "H";
                return (int)d.TotalDays + "D";
            }
        }

        [JsonIgnore]
        public string UpdatedDisplay
        {
            get
            {
                var d = DateTime.UtcNow - UpdatedUtc;
                if (d.TotalMinutes < 2) return "JUST NOW";
                if (d.TotalMinutes < 60) return (int)d.TotalMinutes + " MIN AGO";
                if (d.TotalHours < 24) return (int)d.TotalHours + " HR AGO";
                return UpdatedUtc.ToLocalTime().ToString("dd MMM HH:mm").ToUpperInvariant();
            }
        }

        [JsonIgnore] public string CreatedDisplay => CreatedUtc.ToLocalTime().ToString("dd MMM \u00B7 HH:mm").ToUpperInvariant();

        public void RefreshTimeDependent()
            => OnPropertiesChanged(nameof(AgeDisplay), nameof(UpdatedDisplay), nameof(MessageCount));
    }
}

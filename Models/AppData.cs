using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using NEXUS.Common;

namespace NEXUS.Models
{
    public class ActivityEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public ActivityKind Kind { get; set; }
        public string Message { get; set; } = "";
        public string Detail { get; set; } = "";

        [JsonIgnore] public DateTime Timestamp => TimestampUtc.ToLocalTime();
        [JsonIgnore] public string TimeDisplay => Timestamp.ToString("HH:mm");
        [JsonIgnore] public string DateDisplay => Timestamp.ToString("dd MMM").ToUpperInvariant();

        [JsonIgnore]
        public string KindLabel => Kind switch
        {
            ActivityKind.Created => "CREATE",
            ActivityKind.Completed => "COMPLETE",
            ActivityKind.Reopened => "REOPEN",
            ActivityKind.Edited => "EDIT",
            ActivityKind.Deleted => "DELETE",
            ActivityKind.Priority => "PRIORITY",
            ActivityKind.Status => "STATUS",
            ActivityKind.Focus => "FOCUS",
            ActivityKind.Data => "DATA",
            _ => "SYSTEM"
        };
    }

    public class FocusSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? TaskId { get; set; }
        public string TaskTitle { get; set; } = "";
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
        public int PlannedMinutes { get; set; }
        public int ElapsedSeconds { get; set; }
        public bool Finished { get; set; }
    }

    public class AppSettings : ObservableObject
    {
        private string _theme = "Deep Space";
        private string _accent = "Cyan";
        private bool _notificationsEnabled = true;
        private bool _soundEnabled = true;
        private bool _toastsEnabled = true;
        private int _defaultFocusMinutes = 25;
        private bool _launchOnStartup;
        private bool _restoreLastSection = true;
        private string _lastSection = "OVERVIEW";
        private DateTime _missionDeadline = DateTime.Today.AddHours(23).AddMinutes(59);
        private bool _autoRollDeadline = true;

        public string Theme { get => _theme; set => Set(ref _theme, value); }
        public string Accent { get => _accent; set => Set(ref _accent, value); }
        public bool NotificationsEnabled { get => _notificationsEnabled; set => Set(ref _notificationsEnabled, value); }
        public bool SoundEnabled { get => _soundEnabled; set => Set(ref _soundEnabled, value); }
        public bool ToastsEnabled { get => _toastsEnabled; set => Set(ref _toastsEnabled, value); }
        public int DefaultFocusMinutes { get => _defaultFocusMinutes; set => Set(ref _defaultFocusMinutes, value); }
        public bool LaunchOnStartup { get => _launchOnStartup; set => Set(ref _launchOnStartup, value); }
        public bool RestoreLastSection { get => _restoreLastSection; set => Set(ref _restoreLastSection, value); }
        public string LastSection { get => _lastSection; set => Set(ref _lastSection, value); }

        /// <summary>The current mission deadline. Defaults to the upcoming Wednesday 23:59.</summary>
        public DateTime MissionDeadline { get => _missionDeadline; set => Set(ref _missionDeadline, value); }

        /// <summary>When true, an elapsed deadline rolls forward to the next Wednesday on launch.</summary>
        public bool AutoRollDeadline { get => _autoRollDeadline; set => Set(ref _autoRollDeadline, value); }

        /// <summary>Next Wednesday at 23:59 relative to <paramref name="from"/>. Today counts if it is Wednesday.</summary>
        public static DateTime NextWednesday(DateTime from)
        {
            var offset = ((int)DayOfWeek.Wednesday - (int)from.DayOfWeek + 7) % 7;
            var target = from.Date.AddDays(offset).AddHours(23).AddMinutes(59);
            if (target <= from) target = target.AddDays(7);
            return target;
        }
    }

    /// <summary>Everything persisted to disk, and the shape of the export bundle.</summary>
    public class AppData
    {
        public int SchemaVersion { get; set; } = 4;
        public DateTime SavedUtc { get; set; } = DateTime.UtcNow;

        // --- task subsystem (schema v1) ---
        public ObservableCollection<MissionTask> Tasks { get; set; } = new ObservableCollection<MissionTask>();
        public List<ActivityEntry> Activity { get; set; } = new List<ActivityEntry>();
        public List<FocusSession> FocusSessions { get; set; } = new List<FocusSession>();

        // --- operations subsystems (schema v2) ---
        public ObservableCollection<Device> Devices { get; set; } = new ObservableCollection<Device>();
        public ObservableCollection<NexusUser> Users { get; set; } = new ObservableCollection<NexusUser>();
        public ObservableCollection<ManagedServer> Servers { get; set; } = new ObservableCollection<ManagedServer>();
        public ObservableCollection<AutomationRule> Automations { get; set; } = new ObservableCollection<AutomationRule>();
        // --- simulated company (schema v4) ---
        public ObservableCollection<Employee> Employees { get; set; } = new ObservableCollection<Employee>();
        public ObservableCollection<Workstation> Workstations { get; set; } = new ObservableCollection<Workstation>();
        public ObservableCollection<Ticket> Tickets { get; set; } = new ObservableCollection<Ticket>();

        public ObservableCollection<NexusFolder> Folders { get; set; } = new ObservableCollection<NexusFolder>();
        public ObservableCollection<NexusFile> Files { get; set; } = new ObservableCollection<NexusFile>();
        public List<SystemEvent> Events { get; set; } = new List<SystemEvent>();
        public List<Alert> Alerts { get; set; } = new List<Alert>();

        public AppSettings Settings { get; set; }
    }
}

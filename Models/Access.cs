using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using NEXUS.Common;

namespace NEXUS.Models
{
    public enum UserRole
    {
        Administrator = 0,
        Operator = 1,
        User = 2,
        Guest = 3,
        Suspended = 4
    }

    public enum UserStatus
    {
        Active = 0,
        Idle = 1,
        Offline = 2,
        Suspended = 3
    }

    /// <summary>Capabilities that can be granted or revoked per user.</summary>
    public enum Permission
    {
        ViewSystems = 0,
        ViewDevices = 1,
        ControlDevices = 2,
        ManageDevices = 3,
        ManageServers = 4,
        ControlServices = 5,
        ManageUsers = 6,
        ChangePermissions = 7,
        SecurityConfiguration = 8,
        NetworkConfiguration = 9,
        SystemConfiguration = 10,
        ManageAutomations = 11,
        UseTerminal = 12,
        ManageTasks = 13
    }

    public static class Permissions
    {
        public static readonly Permission[] All =
        {
            Permission.ViewSystems, Permission.ViewDevices, Permission.ControlDevices,
            Permission.ManageDevices, Permission.ManageServers, Permission.ControlServices,
            Permission.ManageUsers, Permission.ChangePermissions, Permission.SecurityConfiguration,
            Permission.NetworkConfiguration, Permission.SystemConfiguration,
            Permission.ManageAutomations, Permission.UseTerminal, Permission.ManageTasks
        };

        public static string Label(Permission p) => p switch
        {
            Permission.ViewSystems => "View Systems",
            Permission.ViewDevices => "View Devices",
            Permission.ControlDevices => "Control Devices",
            Permission.ManageDevices => "Manage Devices",
            Permission.ManageServers => "Manage Servers",
            Permission.ControlServices => "Control Services",
            Permission.ManageUsers => "Manage Users",
            Permission.ChangePermissions => "Change Permissions",
            Permission.SecurityConfiguration => "Security Configuration",
            Permission.NetworkConfiguration => "Network Configuration",
            Permission.SystemConfiguration => "System Configuration",
            Permission.ManageAutomations => "Manage Automations",
            Permission.UseTerminal => "Use Terminal",
            Permission.ManageTasks => "Manage Tasks",
            _ => p.ToString()
        };

        /// <summary>The capability set a role is granted when it is first assigned.</summary>
        public static IEnumerable<Permission> ForRole(UserRole role)
        {
            switch (role)
            {
                case UserRole.Administrator:
                    return All;

                case UserRole.Operator:
                    return new[]
                    {
                        Permission.ViewSystems, Permission.ViewDevices, Permission.ControlDevices,
                        Permission.ManageDevices, Permission.ManageServers, Permission.ControlServices,
                        Permission.ManageAutomations, Permission.UseTerminal, Permission.ManageTasks
                    };

                case UserRole.User:
                    return new[]
                    {
                        Permission.ViewSystems, Permission.ViewDevices,
                        Permission.ControlDevices, Permission.ManageTasks
                    };

                case UserRole.Guest:
                    return new[] { Permission.ViewSystems, Permission.ViewDevices };

                default:
                    return new Permission[0];
            }
        }
    }

    public class UserSession
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Origin { get; set; } = "NEXUS Console";
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

        [JsonIgnore] public string StartedDisplay => StartedUtc.ToLocalTime().ToString("dd MMM HH:mm").ToUpperInvariant();

        [JsonIgnore]
        public string DurationDisplay
        {
            get
            {
                var d = DateTime.UtcNow - StartedUtc;
                if (d.TotalMinutes < 1) return "JUST NOW";
                if (d.TotalHours < 1) return (int)d.TotalMinutes + "M";
                if (d.TotalDays < 1) return (int)d.TotalHours + "H " + d.Minutes + "M";
                return (int)d.TotalDays + "D";
            }
        }
    }

    public class NexusUser : ObservableObject
    {
        private string _displayName = "";
        private UserRole _role = UserRole.User;
        private UserStatus _status = UserStatus.Offline;
        private DateTime? _lastLoginUtc;
        private ObservableCollection<Permission> _granted = new ObservableCollection<Permission>();

        public Guid Id { get; set; } = Guid.NewGuid();
        public string Username { get; set; } = "";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>The operator running this NEXUS install. Cannot be suspended or deleted.</summary>
        public bool IsPrimary { get; set; }

        public string DisplayName
        {
            get => _displayName;
            set { if (Set(ref _displayName, value)) OnPropertyChanged(nameof(Initials)); }
        }

        public UserRole Role
        {
            get => _role;
            set
            {
                if (Set(ref _role, value))
                    OnPropertiesChanged(nameof(RoleLabel), nameof(RoleBrushKey), nameof(AccessLevel));
            }
        }

        public UserStatus Status
        {
            get => _status;
            set
            {
                if (Set(ref _status, value))
                    OnPropertiesChanged(nameof(StatusLabel), nameof(StatusBrushKey), nameof(IsSuspended));
            }
        }

        public DateTime? LastLoginUtc
        {
            get => _lastLoginUtc;
            set { if (Set(ref _lastLoginUtc, value)) OnPropertyChanged(nameof(LastLoginDisplay)); }
        }

        public ObservableCollection<Permission> Granted
        {
            get => _granted;
            set { Set(ref _granted, value ?? new ObservableCollection<Permission>()); RefreshPermissions(); }
        }

        public ObservableCollection<UserSession> Sessions { get; set; } = new ObservableCollection<UserSession>();

        // ---- display ----

        [JsonIgnore] public bool IsSuspended => Status == UserStatus.Suspended || Role == UserRole.Suspended;
        [JsonIgnore] public int PermissionCount => Granted.Count;
        [JsonIgnore] public int SessionCount => Sessions.Count;

        [JsonIgnore]
        public string Initials
        {
            get
            {
                var source = string.IsNullOrWhiteSpace(DisplayName) ? Username : DisplayName;
                if (string.IsNullOrWhiteSpace(source)) return "??";
                var parts = source.Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
                return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
            }
        }

        [JsonIgnore]
        public string RoleLabel => Role switch
        {
            UserRole.Administrator => "ADMINISTRATOR",
            UserRole.Operator => "OPERATOR",
            UserRole.User => "USER",
            UserRole.Guest => "GUEST",
            _ => "SUSPENDED"
        };

        [JsonIgnore]
        public string RoleBrushKey => Role switch
        {
            UserRole.Administrator => "BrushAccent",
            UserRole.Operator => "BrushLow",
            UserRole.User => "BrushNormal",
            UserRole.Guest => "BrushPlanned",
            _ => "BrushCritical"
        };

        [JsonIgnore]
        public string AccessLevel => Role switch
        {
            UserRole.Administrator => "FULL SYSTEM ACCESS",
            UserRole.Operator => "OPERATIONS ACCESS",
            UserRole.User => "STANDARD ACCESS",
            UserRole.Guest => "READ-ONLY ACCESS",
            _ => "ACCESS REVOKED"
        };

        [JsonIgnore]
        public string StatusLabel => Status switch
        {
            UserStatus.Active => "ACTIVE",
            UserStatus.Idle => "IDLE",
            UserStatus.Suspended => "SUSPENDED",
            _ => "OFFLINE"
        };

        [JsonIgnore]
        public string StatusBrushKey => Status switch
        {
            UserStatus.Active => "BrushCompleted",
            UserStatus.Idle => "BrushNormal",
            UserStatus.Suspended => "BrushCritical",
            _ => "BrushTextMuted"
        };

        [JsonIgnore]
        public string LastLoginDisplay
        {
            get
            {
                if (!LastLoginUtc.HasValue) return "NEVER";
                var d = DateTime.UtcNow - LastLoginUtc.Value;
                if (d.TotalMinutes < 2) return "NOW";
                if (d.TotalHours < 1) return (int)d.TotalMinutes + " MIN AGO";
                if (d.TotalHours < 24) return (int)d.TotalHours + " HR AGO";
                return LastLoginUtc.Value.ToLocalTime().ToString("dd MMM HH:mm").ToUpperInvariant();
            }
        }

        public bool Has(Permission p) => !IsSuspended && Granted.Contains(p);

        public void RefreshPermissions()
            => OnPropertiesChanged(nameof(PermissionCount), nameof(Granted));

        public void RefreshTimeDependent()
            => OnPropertiesChanged(nameof(LastLoginDisplay), nameof(SessionCount));
    }

    /// <summary>One cell of the access matrix, bound directly by the permissions grid.</summary>
    public class PermissionCell : ObservableObject
    {
        private bool _isAllowed;

        public NexusUser User { get; set; }
        public Permission Permission { get; set; }
        public bool IsLocked { get; set; }

        public bool IsAllowed
        {
            get => _isAllowed;
            set => Set(ref _isAllowed, value);
        }

        public string Mark => IsAllowed ? "\u2713" : "\u2013";
    }

    public class PermissionRow : ObservableObject
    {
        public Permission Permission { get; set; }
        public string Label { get; set; } = "";
        public ObservableCollection<PermissionCell> Cells { get; } = new ObservableCollection<PermissionCell>();
    }
}

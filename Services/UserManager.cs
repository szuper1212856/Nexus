using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NEXUS.Models;

namespace NEXUS.Services
{
    /// <summary>Directory of NEXUS accounts, their sessions and their sign-in history.</summary>
    public class UserManager
    {
        private readonly DataStore _store;
        private readonly EventBus _bus;

        public UserManager(DataStore store, EventBus bus)
        {
            _store = store;
            _bus = bus;
        }

        public ObservableCollection<NexusUser> Users => _store.Data.Users;

        public event Action Changed;

        public NexusUser Current { get; private set; }

        public int ActiveCount => Users.Count(u => u.Status == UserStatus.Active);
        public int SuspendedCount => Users.Count(u => u.IsSuspended);
        public int SessionCount => Users.Sum(u => u.Sessions.Count);

        public NexusUser Find(string name)
            => Users.FirstOrDefault(u => string.Equals(u.Username, name, StringComparison.OrdinalIgnoreCase))
               ?? Users.FirstOrDefault(u => u.DisplayName != null &&
                       u.DisplayName.IndexOf(name ?? "", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Opens the console session for the primary operator at start-up.</summary>
        public void SignInPrimary()
        {
            var primary = Users.FirstOrDefault(u => u.IsPrimary) ?? Users.FirstOrDefault();
            if (primary == null) return;

            Current = primary;
            _bus.CurrentActor = primary.DisplayName;

            primary.Status = UserStatus.Active;
            primary.LastLoginUtc = DateTime.UtcNow;
            primary.Sessions.Add(new UserSession { Origin = "NEXUS Console \u00B7 " + Environment.MachineName });

            _bus.Publish(EventCategory.Auth, primary.DisplayName + " authenticated",
                "Console session opened on " + Environment.MachineName,
                EventSeverity.Notice, "AUTH", primary.DisplayName);

            _bus.Signal(TriggerType.UserAuthenticated, primary.DisplayName, "Console session", primary);
            Touch();
        }

        public void Add(NexusUser user)
        {
            if (user == null) return;

            user.Granted = new ObservableCollection<Permission>(Permissions.ForRole(user.Role));
            Users.Add(user);

            _bus.Publish(EventCategory.Access, "Account created",
                user.DisplayName + " \u00B7 " + user.RoleLabel, EventSeverity.Notice, "USERS");
            Touch();
        }

        public void Remove(NexusUser user)
        {
            if (user == null || user.IsPrimary || !Users.Contains(user)) return;

            Users.Remove(user);
            _bus.Publish(EventCategory.Access, "Account removed", user.DisplayName,
                         EventSeverity.Warning, "USERS");
            Touch();
        }

        public void SetRole(NexusUser user, UserRole role)
        {
            if (user == null || user.Role == role) return;
            if (user.IsPrimary && role != UserRole.Administrator) return;

            var previous = user.RoleLabel;
            user.Role = role;
            user.Granted = new ObservableCollection<Permission>(Permissions.ForRole(role));

            if (role == UserRole.Suspended) Suspend(user, "Role changed to suspended");
            else if (user.Status == UserStatus.Suspended) user.Status = UserStatus.Offline;

            _bus.Publish(EventCategory.Access, "Role changed",
                user.DisplayName + " \u00B7 " + previous + " \u2192 " + user.RoleLabel,
                EventSeverity.Warning, "ACCESS");
            Touch();
        }

        public void Suspend(NexusUser user, string reason = "")
        {
            if (user == null || user.IsPrimary) return;

            user.Status = UserStatus.Suspended;
            _bus.Publish(EventCategory.Security, "Account suspended",
                user.DisplayName + (string.IsNullOrWhiteSpace(reason) ? "" : " \u00B7 " + reason),
                EventSeverity.Critical, "SECURITY");

            _bus.Signal(TriggerType.UserSuspended, user.DisplayName, reason, user);
            Touch();
        }

        public void Reinstate(NexusUser user)
        {
            if (user == null || !user.IsSuspended) return;

            user.Status = UserStatus.Offline;
            if (user.Role == UserRole.Suspended) user.Role = UserRole.Guest;

            _bus.Publish(EventCategory.Security, "Account reinstated",
                user.DisplayName + " \u00B7 " + user.RoleLabel, EventSeverity.Warning, "SECURITY");
            Touch();
        }

        public int RevokeSessions(NexusUser user)
        {
            if (user == null) return 0;

            var count = user.Sessions.Count;
            if (count == 0) return 0;

            user.Sessions.Clear();
            if (user.Status == UserStatus.Active) user.Status = UserStatus.Offline;
            user.RefreshTimeDependent();

            _bus.Publish(EventCategory.Security, "Sessions revoked",
                count + " session(s) closed for " + user.DisplayName, EventSeverity.Warning, "SECURITY");
            Touch();
            return count;
        }

        public IEnumerable<UserSession> AllSessions()
            => Users.SelectMany(u => u.Sessions);

        public void RefreshTimeDependent()
        {
            foreach (var u in Users) u.RefreshTimeDependent();
        }

        public void Touch()
        {
            Changed?.Invoke();
            _store.RequestSave();
        }

        public void SeedDefaults(string operatorName)
        {
            var admin = new NexusUser
            {
                Username = "balazs",
                DisplayName = string.IsNullOrWhiteSpace(operatorName) ? "Operator" : operatorName,
                Role = UserRole.Administrator,
                Status = UserStatus.Offline,
                IsPrimary = true
            };
            admin.Granted = new ObservableCollection<Permission>(Permissions.ForRole(UserRole.Administrator));
            Users.Add(admin);

            void Add(string username, string display, UserRole role, UserStatus status, int lastLoginHoursAgo)
            {
                var u = new NexusUser
                {
                    Username = username,
                    DisplayName = display,
                    Role = role,
                    Status = status,
                    LastLoginUtc = lastLoginHoursAgo < 0 ? (DateTime?)null : DateTime.UtcNow.AddHours(-lastLoginHoursAgo)
                };
                u.Granted = new ObservableCollection<Permission>(Permissions.ForRole(role));
                if (status == UserStatus.Active)
                    u.Sessions.Add(new UserSession { Origin = "Remote console", StartedUtc = DateTime.UtcNow.AddMinutes(-40) });
                Users.Add(u);
            }

            Add("operator", "Ops Console", UserRole.Operator, UserStatus.Active, 1);
            Add("test", "Test User", UserRole.User, UserStatus.Idle, 20);
            Add("guest", "Guest Access", UserRole.Guest, UserStatus.Offline, -1);

            _bus.Publish(EventCategory.Access, "Account directory provisioned",
                Users.Count + " accounts registered", EventSeverity.Notice, "USERS");
            Touch();
        }
    }

    /// <summary>
    /// Governs the permission matrix. Every change is written to the audit trail with the
    /// before state, the after state and the account that authorised it.
    /// </summary>
    public class AccessManager
    {
        private readonly DataStore _store;
        private readonly EventBus _bus;
        private readonly UserManager _users;

        public AccessManager(DataStore store, EventBus bus, UserManager users)
        {
            _store = store;
            _bus = bus;
            _users = users;
        }

        public event Action Changed;

        /// <summary>Details of the most recent change, shown as a receipt in the UI.</summary>
        public AccessChangeReceipt LastChange { get; private set; }

        public bool CanCurrentUserManageAccess()
            => _users.Current == null || _users.Current.Has(Permission.ChangePermissions);

        /// <summary>Applies a permission change. Returns false when the caller is not authorised.</summary>
        public bool SetPermission(NexusUser target, Permission permission, bool allowed)
        {
            if (target == null) return false;

            if (!CanCurrentUserManageAccess())
            {
                _bus.Publish(EventCategory.Security, "Access change denied",
                    "The signed-in account lacks Change Permissions.", EventSeverity.Critical, "ACCESS");
                return false;
            }

            if (target.IsPrimary && !allowed)
            {
                _bus.Publish(EventCategory.Security, "Access change refused",
                    "The primary administrator cannot have permissions revoked.",
                    EventSeverity.Warning, "ACCESS");
                return false;
            }

            var was = target.Granted.Contains(permission);
            if (was == allowed) return true;

            if (allowed) target.Granted.Add(permission);
            else target.Granted.Remove(permission);

            target.RefreshPermissions();

            var authoriser = _users.Current?.DisplayName ?? "SYSTEM";
            LastChange = new AccessChangeReceipt
            {
                UserName = target.DisplayName,
                PermissionName = Permissions.Label(permission),
                Previous = was ? "ALLOWED" : "DENIED",
                Next = allowed ? "ALLOWED" : "DENIED",
                Authoriser = authoriser,
                TimestampUtc = DateTime.UtcNow
            };

            _bus.Publish(EventCategory.Access, "Access policy updated",
                target.DisplayName + " \u00B7 " + Permissions.Label(permission) + " \u00B7 " +
                LastChange.Previous + " \u2192 " + LastChange.Next + " \u00B7 authorised by " + authoriser,
                EventSeverity.Warning, "ACCESS", authoriser);

            _bus.Signal(TriggerType.PermissionChanged, target.DisplayName, Permissions.Label(permission), target);

            Changed?.Invoke();
            _store.RequestSave();
            return true;
        }

        /// <summary>Resets a user's grants back to the defaults for their role.</summary>
        public void ApplyRoleDefaults(NexusUser target)
        {
            if (target == null || !CanCurrentUserManageAccess()) return;

            target.Granted = new ObservableCollection<Permission>(Permissions.ForRole(target.Role));
            target.RefreshPermissions();

            _bus.Publish(EventCategory.Access, "Role defaults reapplied",
                target.DisplayName + " \u00B7 " + target.RoleLabel, EventSeverity.Warning, "ACCESS");

            Changed?.Invoke();
            _store.RequestSave();
        }
    }

    public class AccessChangeReceipt
    {
        public string UserName { get; set; } = "";
        public string PermissionName { get; set; } = "";
        public string Previous { get; set; } = "";
        public string Next { get; set; } = "";
        public string Authoriser { get; set; } = "";
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        public string TimeDisplay => TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
        public string StatusLabel => "SUCCESS";
        public string NextBrushKey => Next == "ALLOWED" ? "BrushCompleted" : "BrushCritical";
    }
}

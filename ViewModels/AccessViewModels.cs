using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using NEXUS.Common;
using NEXUS.Models;
using NEXUS.Services;

namespace NEXUS.ViewModels
{
    public class UsersViewModel : ViewModelBase
    {
        private readonly UserManager _users;
        private NexusUser _selected;

        public UsersViewModel(ShellViewModel shell)
        {
            Shell = shell;
            _users = AppServices.Users;
            _users.Changed += Push;

            SelectCommand = new RelayCommand(p => { if (p is NexusUser u) Selected = u; });
            SuspendCommand = new RelayCommand(_ => Suspend(), _ => Selected != null && !Selected.IsPrimary);
            ReinstateCommand = new RelayCommand(_ => _users.Reinstate(Selected),
                                                _ => Selected != null && Selected.IsSuspended);
            RevokeCommand = new RelayCommand(_ => Revoke(), _ => Selected != null && Selected.Sessions.Count > 0);
            SetRoleCommand = new RelayCommand(p =>
            {
                if (Selected != null && Enum.TryParse<UserRole>(p?.ToString(), out var role))
                    _users.SetRole(Selected, role);
            });
            RemoveCommand = new RelayCommand(_ => Remove(), _ => Selected != null && !Selected.IsPrimary);
            OpenAccessCommand = new RelayCommand(_ => Shell.OpenAccessFor(Selected));

            Selected = _users.Users.FirstOrDefault();
        }

        public ShellViewModel Shell { get; }
        public ObservableCollection<NexusUser> Users => _users.Users;

        public ICommand SelectCommand { get; }
        public ICommand SuspendCommand { get; }
        public ICommand ReinstateCommand { get; }
        public ICommand RevokeCommand { get; }
        public ICommand SetRoleCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand OpenAccessCommand { get; }

        public NexusUser Selected
        {
            get => _selected;
            set
            {
                if (Set(ref _selected, value))
                    OnPropertiesChanged(nameof(HasSelection), nameof(SelectedPermissions), nameof(SelectedRoleKey));
            }
        }

        public bool HasSelection => Selected != null;
        public string SelectedRoleKey => Selected?.Role.ToString() ?? "";

        /// <summary>Full capability list with the selected user's grants applied, for the detail panel.</summary>
        public IEnumerable<PermissionCell> SelectedPermissions
        {
            get
            {
                if (Selected == null) return Enumerable.Empty<PermissionCell>();
                return Permissions.All.Select(p => new PermissionCell
                {
                    User = Selected,
                    Permission = p,
                    IsAllowed = Selected.Granted.Contains(p)
                }).ToList();
            }
        }

        public int ActiveCount => _users.ActiveCount;
        public int SuspendedCount => _users.SuspendedCount;
        public int SessionCount => _users.SessionCount;
        public int TotalCount => _users.Users.Count;

        public override void OnActivated() { _users.RefreshTimeDependent(); Push(); }

        public override void OnTick() => _users.RefreshTimeDependent();

        public void SelectPayload(object payload)
        {
            if (payload is NexusUser u) Selected = u;
        }

        private void Suspend()
        {
            if (Selected == null) return;
            if (!DialogService.Confirm("SUSPEND ACCOUNT",
                    "\"" + Selected.DisplayName + "\" will lose access and any active sessions will be revoked.",
                    "SUSPEND"))
                return;

            _users.Suspend(Selected, "Suspended by " + (_users.Current?.DisplayName ?? "operator"));
            Push();
        }

        private void Revoke()
        {
            if (Selected == null) return;
            var n = _users.RevokeSessions(Selected);
            AppServices.Toasts.Show("SESSIONS REVOKED", n + " session(s) closed for " + Selected.DisplayName,
                                    ToastKind.Warning);
            Push();
        }

        private void Remove()
        {
            var target = Selected;
            if (target == null) return;

            if (!DialogService.Confirm("DELETE ACCOUNT",
                    "\"" + target.DisplayName + "\" will be permanently removed from the directory.", "DELETE"))
                return;

            _users.Remove(target);
            Selected = _users.Users.FirstOrDefault();
        }

        private void Push()
            => OnPropertiesChanged(nameof(ActiveCount), nameof(SuspendedCount), nameof(SessionCount),
                                   nameof(TotalCount), nameof(SelectedPermissions), nameof(SelectedRoleKey));
    }

    /// <summary>
    /// The permissions matrix. Rows are capabilities, columns are accounts; toggling a
    /// cell writes an audit entry and produces an on-screen policy receipt.
    /// </summary>
    public class AccessViewModel : ViewModelBase
    {
        private readonly UserManager _users;
        private readonly AccessManager _access;

        public AccessViewModel()
        {
            _users = AppServices.Users;
            _access = AppServices.Access;

            _users.Changed += Rebuild;
            _access.Changed += OnAccessChanged;

            ToggleCommand = new RelayCommand(p => Toggle(p as PermissionCell));
            ApplyDefaultsCommand = new RelayCommand(p =>
            {
                if (p is NexusUser u) _access.ApplyRoleDefaults(u);
            });

            Rebuild();
        }

        public ObservableCollection<PermissionRow> Rows { get; } = new ObservableCollection<PermissionRow>();
        public ObservableCollection<NexusUser> Columns { get; } = new ObservableCollection<NexusUser>();

        public ICommand ToggleCommand { get; }
        public ICommand ApplyDefaultsCommand { get; }

        public AccessChangeReceipt LastChange => _access.LastChange;
        public bool HasReceipt => _access.LastChange != null;
        public bool CanManage => _access.CanCurrentUserManageAccess();

        public string AuthorityLabel => CanManage
            ? "AUTHORISED \u00B7 " + (_users.Current?.DisplayName ?? "OPERATOR")
            : "READ ONLY \u00B7 INSUFFICIENT PRIVILEGE";

        public string AuthorityBrushKey => CanManage ? "BrushCompleted" : "BrushCritical";

        public override void OnActivated() => Rebuild();

        private void Toggle(PermissionCell cell)
        {
            if (cell == null || cell.User == null) return;

            var next = !cell.IsAllowed;
            if (!_access.SetPermission(cell.User, cell.Permission, next))
            {
                AppServices.Toasts.Show("CHANGE REFUSED",
                    cell.User.IsPrimary
                        ? "The primary administrator retains full access."
                        : "This account is not authorised to change permissions.",
                    ToastKind.Warning);
                Rebuild();
                return;
            }

            cell.IsAllowed = next;

            AppServices.Toasts.Show("ACCESS POLICY UPDATED",
                cell.User.DisplayName + " \u00B7 " + Permissions.Label(cell.Permission) + " \u00B7 " +
                (next ? "ALLOWED" : "DENIED"),
                next ? ToastKind.Success : ToastKind.Warning);

            OnPropertiesChanged(nameof(LastChange), nameof(HasReceipt));
        }

        private void OnAccessChanged()
            => OnPropertiesChanged(nameof(LastChange), nameof(HasReceipt), nameof(CanManage),
                                   nameof(AuthorityLabel), nameof(AuthorityBrushKey));

        private void Rebuild()
        {
            Columns.Clear();
            foreach (var u in _users.Users) Columns.Add(u);

            Rows.Clear();
            foreach (var p in Permissions.All)
            {
                var row = new PermissionRow { Permission = p, Label = Permissions.Label(p) };
                foreach (var u in _users.Users)
                    row.Cells.Add(new PermissionCell
                    {
                        User = u,
                        Permission = p,
                        IsAllowed = u.Granted.Contains(p),
                        IsLocked = u.IsPrimary
                    });
                Rows.Add(row);
            }

            OnPropertiesChanged(nameof(CanManage), nameof(AuthorityLabel), nameof(AuthorityBrushKey),
                                nameof(LastChange), nameof(HasReceipt));
        }
    }

    public class SecurityViewModel : ViewModelBase
    {
        private readonly SecurityManager _security;
        private readonly EventBus _bus;
        private readonly UserManager _users;

        public SecurityViewModel()
        {
            _security = AppServices.Security;
            _bus = AppServices.Bus;
            _users = AppServices.Users;

            _bus.Published += _ => Push();
            _bus.AlertsChanged += Push;
            _users.Changed += Push;

            AcknowledgeCommand = new RelayCommand(p => { if (p is Alert a) _bus.Acknowledge(a); });
            AcknowledgeAllCommand = new RelayCommand(_ => _bus.AcknowledgeAll());
            RevokeCommand = new RelayCommand(p => { if (p is NexusUser u) _users.RevokeSessions(u); });
            ReinstateCommand = new RelayCommand(p => { if (p is NexusUser u) _users.Reinstate(u); });

            Push();
        }

        public ICommand AcknowledgeCommand { get; }
        public ICommand AcknowledgeAllCommand { get; }
        public ICommand RevokeCommand { get; }
        public ICommand ReinstateCommand { get; }

        public ObservableCollection<Alert> Alerts { get; } = new ObservableCollection<Alert>();
        public ObservableCollection<SystemEvent> AuthTrail { get; } = new ObservableCollection<SystemEvent>();
        public ObservableCollection<NexusUser> Sessions { get; } = new ObservableCollection<NexusUser>();
        public ObservableCollection<NexusUser> Suspended { get; } = new ObservableCollection<NexusUser>();

        public string PostureLabel => _security.PostureLabel;
        public string PostureBrushKey => _security.PostureBrushKey;
        public string PostureDetail => _security.PostureDetail;
        public int OpenAlerts => _security.OpenAlerts;
        public int CriticalEvents => _security.CriticalEvents;
        public int SuspendedUsers => _security.SuspendedUsers;
        public int ActiveSessions => _security.ActiveSessions;

        public override void OnActivated() => Push();

        public override void OnTick() => _users.RefreshTimeDependent();

        private void Push()
        {
            Alerts.Clear();
            foreach (var a in _bus.Alerts.Take(12)) Alerts.Add(a);

            AuthTrail.Clear();
            foreach (var e in _security.AuthEvents().Take(18)) AuthTrail.Add(e);

            Sessions.Clear();
            foreach (var u in _users.Users.Where(u => u.Sessions.Count > 0)) Sessions.Add(u);

            Suspended.Clear();
            foreach (var u in _users.Users.Where(u => u.IsSuspended)) Suspended.Add(u);

            OnPropertiesChanged(nameof(PostureLabel), nameof(PostureBrushKey), nameof(PostureDetail),
                nameof(OpenAlerts), nameof(CriticalEvents), nameof(SuspendedUsers), nameof(ActiveSessions));
        }
    }
}

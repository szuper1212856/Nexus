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
    /// <summary>
    /// The company directory. Every screen here is an administrative surface: selecting an
    /// employee exposes the actions available against their simulated account and device.
    /// </summary>
    public class DirectoryViewModel : ViewModelBase
    {
        private readonly SimulationEngine _sim;
        private readonly EventBus _bus;

        private Employee _selected;
        private string _search = "";
        private string _departmentFilter = "ALL";
        private string _statusFilter = "ALL";

        public DirectoryViewModel(ShellViewModel shell)
        {
            Shell = shell;
            _sim = AppServices.Simulation;
            _bus = AppServices.Bus;
            _sim.Changed += Refresh;

            SelectCommand = new RelayCommand(p => { if (p is Employee e) Selected = e; });
            SetDepartmentFilterCommand = new RelayCommand(p => DepartmentFilter = p?.ToString() ?? "ALL");
            SetStatusFilterCommand = new RelayCommand(p => StatusFilter = p?.ToString() ?? "ALL");

            ResetPasswordCommand = new RelayCommand(_ => ResetPassword(), _ => Selected != null);
            UnlockCommand = new RelayCommand(_ => _sim.Unlock(Selected, Actor),
                                             _ => Selected != null && Selected.AccountState == AccountState.Locked);
            DisableCommand = new RelayCommand(_ => SetState(AccountState.Disabled),
                                              _ => Selected != null && Selected.AccountState != AccountState.Disabled);
            EnableCommand = new RelayCommand(_ => SetState(AccountState.Active),
                                             _ => Selected != null && Selected.AccountState != AccountState.Active);
            ResetMfaCommand = new RelayCommand(_ => _sim.SetMfa(Selected, false, Actor),
                                               _ => Selected != null && Selected.MfaEnabled);
            EnrollMfaCommand = new RelayCommand(_ => _sim.SetMfa(Selected, true, Actor),
                                                _ => Selected != null && !Selected.MfaEnabled);
            ChangeDepartmentCommand = new RelayCommand(p =>
            {
                if (Selected != null && Enum.TryParse<Department>(p?.ToString(), out var d))
                    _sim.SetDepartment(Selected, d, Actor);
            });

            OpenTicketCommand = new RelayCommand(p => { if (p is Ticket t) Shell.OpenTicket(t); });
            OpenServiceDeskCommand = new RelayCommand(_ => Shell.Navigate("SERVICEDESK"));
            RemoteSupportCommand = new RelayCommand(_ => RemoteSupportUnavailable(), _ => Selected != null);

            Selected = _sim.Employees.FirstOrDefault();
            Refresh();
        }

        public ShellViewModel Shell { get; }

        public ObservableCollection<Employee> Visible { get; } = new ObservableCollection<Employee>();
        public ObservableCollection<Ticket> SelectedTickets { get; } = new ObservableCollection<Ticket>();
        public ObservableCollection<SystemEvent> SelectedEvents { get; } = new ObservableCollection<SystemEvent>();

        public ICommand SelectCommand { get; }
        public ICommand SetDepartmentFilterCommand { get; }
        public ICommand SetStatusFilterCommand { get; }
        public ICommand ResetPasswordCommand { get; }
        public ICommand UnlockCommand { get; }
        public ICommand DisableCommand { get; }
        public ICommand EnableCommand { get; }
        public ICommand ResetMfaCommand { get; }
        public ICommand EnrollMfaCommand { get; }
        public ICommand ChangeDepartmentCommand { get; }
        public ICommand OpenTicketCommand { get; }
        public ICommand OpenServiceDeskCommand { get; }
        public ICommand RemoteSupportCommand { get; }

        public IReadOnlyList<string> DepartmentFilters { get; } = new[]
        {
            "ALL", "IT", "Engineering", "Finance", "HR", "Marketing", "Sales", "Management", "Operations"
        };

        public IReadOnlyList<string> StatusFilters { get; } = new[]
        {
            "ALL", "ACTIVE", "DISABLED", "LOCKED", "ONLINE", "OPEN TICKETS"
        };

        public IReadOnlyList<string> Departments { get; } = new[]
        {
            "IT", "Engineering", "Finance", "HR", "Marketing", "Sales", "Management", "Operations"
        };

        private static string Actor => AppServices.Users.Current?.DisplayName ?? "Administrator";

        public Employee Selected
        {
            get => _selected;
            set
            {
                if (!Set(ref _selected, value)) return;
                LoadDetail();
                OnPropertiesChanged(nameof(HasSelection), nameof(SelectedWorkstation),
                                    nameof(OpenTicketCount), nameof(ResolvedTicketCount), nameof(GroupList));
            }
        }

        public bool HasSelection => Selected != null;
        public Workstation SelectedWorkstation => _sim.WorkstationOf(Selected);
        public string GroupList => Selected == null ? "" : string.Join(", ", Selected.Groups);
        public int OpenTicketCount => SelectedTickets.Count(t => t.IsOpen);
        public int ResolvedTicketCount => SelectedTickets.Count(t => t.IsResolved);

        public string Search
        {
            get => _search;
            set { if (Set(ref _search, value)) Refresh(); }
        }

        public string DepartmentFilter
        {
            get => _departmentFilter;
            set { if (Set(ref _departmentFilter, value)) Refresh(); }
        }

        public string StatusFilter
        {
            get => _statusFilter;
            set { if (Set(ref _statusFilter, value)) Refresh(); }
        }

        public string CompanyName => SimulationEngine.CompanyName;
        public int EmployeeCount => _sim.EmployeeCount;
        public int OnlineCount => _sim.OnlineEmployees;
        public int DisabledCount => _sim.DisabledAccounts;
        public int LockedCount => _sim.LockedAccounts;

        public override void OnActivated() => Refresh();

        public override void OnTick() => _sim.RefreshTimeDependent();

        public void SelectPayload(object payload)
        {
            if (payload is Employee e) Selected = e;
        }

        private void SetState(AccountState state)
        {
            if (Selected == null) return;

            if (state == AccountState.Disabled &&
                !DialogService.Confirm("DISABLE ACCOUNT",
                    Selected.FullName + " will be signed out and unable to access their workstation.", "DISABLE"))
                return;

            _sim.SetAccountState(Selected, state, Actor);
            LoadDetail();
        }

        private void ResetPassword()
        {
            if (Selected == null) return;

            if (!DialogService.Confirm("RESET PASSWORD",
                    "The simulated account for " + Selected.FullName
                    + " will be marked as requiring a new password at next sign-in.", "RESET"))
                return;

            _sim.ResetPassword(Selected, Actor);
            AppServices.Toasts.Show("PASSWORD RESET",
                Selected.FullName + " \u00B7 account marked for password change", ToastKind.Warning);
            LoadDetail();
        }

        /// <summary>
        /// Remote desktop simulation is a later phase. Rather than offering a button that
        /// does nothing, the console says plainly that it is not built yet.
        /// </summary>
        private void RemoteSupportUnavailable()
            => AppServices.Toasts.Show("REMOTE SUPPORT NOT AVAILABLE",
                "The simulated workstation console has not been implemented yet.", ToastKind.Warning);

        private void LoadDetail()
        {
            SelectedTickets.Clear();
            SelectedEvents.Clear();
            if (Selected == null) return;

            foreach (var t in _sim.TicketsFor(Selected).OrderByDescending(t => t.UpdatedUtc))
                SelectedTickets.Add(t);

            foreach (var e in _bus.Events
                         .Where(e => (e.Detail ?? "").IndexOf(Selected.FullName, StringComparison.OrdinalIgnoreCase) >= 0
                                  || (e.Message ?? "").IndexOf(Selected.FullName, StringComparison.OrdinalIgnoreCase) >= 0)
                         .Take(8))
                SelectedEvents.Add(e);

            OnPropertiesChanged(nameof(OpenTicketCount), nameof(ResolvedTicketCount),
                                nameof(SelectedWorkstation), nameof(GroupList));
        }

        private void Refresh()
        {
            IEnumerable<Employee> source = _sim.Employees;

            if (DepartmentFilter != "ALL" && Enum.TryParse<Department>(DepartmentFilter, out var dept))
                source = source.Where(e => e.Department == dept);

            source = StatusFilter switch
            {
                "ACTIVE" => source.Where(e => e.AccountState == AccountState.Active),
                "DISABLED" => source.Where(e => e.AccountState == AccountState.Disabled),
                "LOCKED" => source.Where(e => e.AccountState == AccountState.Locked),
                "ONLINE" => source.Where(e => e.IsOnline && e.CanSignIn),
                "OPEN TICKETS" => source.Where(e => _sim.TicketsFor(e).Any(t => t.IsOpen)),
                _ => source
            };

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var q = Search.Trim();
                source = source.Where(e =>
                    e.FullName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.Username.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.Email.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    e.JobTitle.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (e.WorkstationTag ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            Visible.Clear();
            foreach (var e in source.OrderBy(e => e.LastName)) Visible.Add(e);

            OnPropertiesChanged(nameof(EmployeeCount), nameof(OnlineCount),
                                nameof(DisabledCount), nameof(LockedCount));
        }
    }

    /// <summary>
    /// The service desk queue and ticket console. Each ticket links straight through to the
    /// requester, their device and the audit trail, so investigation never dead-ends.
    /// </summary>
    public class ServiceDeskViewModel : ViewModelBase
    {
        private readonly SimulationEngine _sim;

        private Ticket _selected;
        private string _queue = "OPEN";
        private string _search = "";
        private string _reply = "";

        public ServiceDeskViewModel(ShellViewModel shell)
        {
            Shell = shell;
            _sim = AppServices.Simulation;
            _sim.Changed += Refresh;

            SelectCommand = new RelayCommand(p => { if (p is Ticket t) Selected = t; });
            SetQueueCommand = new RelayCommand(p => Queue = p?.ToString() ?? "OPEN");

            AssignToMeCommand = new RelayCommand(_ => _sim.Assign(Selected, Technician), _ => Selected != null);
            UnassignCommand = new RelayCommand(_ => _sim.Assign(Selected, ""), _ => Selected?.IsAssigned == true);
            SendCommand = new RelayCommand(_ => Send(), _ => Selected != null && !string.IsNullOrWhiteSpace(Reply));
            SetStatusCommand = new RelayCommand(p =>
            {
                if (Selected != null && Enum.TryParse<TicketStatus>(p?.ToString(), out var s))
                    _sim.SetStatus(Selected, s);
            });
            SetPriorityCommand = new RelayCommand(p =>
            {
                if (Selected != null && Enum.TryParse<TicketPriority>(p?.ToString(), out var pr))
                    _sim.SetPriority(Selected, pr);
            });
            ResolveCommand = new RelayCommand(_ => Resolve(), _ => Selected?.IsOpen == true);

            ViewUserCommand = new RelayCommand(_ => Shell.OpenEmployee(Requester), _ => Requester != null);
            ViewEventsCommand = new RelayCommand(_ => Shell.Navigate("LOGS"));

            Selected = _sim.Tickets.FirstOrDefault(t => t.IsOpen);
            Refresh();
        }

        public ShellViewModel Shell { get; }

        public ObservableCollection<Ticket> Visible { get; } = new ObservableCollection<Ticket>();

        public ICommand SelectCommand { get; }
        public ICommand SetQueueCommand { get; }
        public ICommand AssignToMeCommand { get; }
        public ICommand UnassignCommand { get; }
        public ICommand SendCommand { get; }
        public ICommand SetStatusCommand { get; }
        public ICommand SetPriorityCommand { get; }
        public ICommand ResolveCommand { get; }
        public ICommand ViewUserCommand { get; }
        public ICommand ViewEventsCommand { get; }

        public IReadOnlyList<string> Queues { get; } = new[]
        {
            "OPEN", "MY QUEUE", "UNASSIGNED", "IN PROGRESS", "WAITING", "CRITICAL", "RESOLVED", "ALL"
        };

        public IReadOnlyList<string> Statuses { get; } = new[]
        {
            "Open", "InProgress", "WaitingForUser", "Resolved", "Closed"
        };

        public IReadOnlyList<string> Priorities { get; } = new[] { "Low", "Normal", "High", "Critical" };

        private static string Technician => AppServices.Users.Current?.DisplayName ?? "Administrator";

        public Ticket Selected
        {
            get => _selected;
            set
            {
                if (!Set(ref _selected, value)) return;
                Reply = "";
                OnPropertiesChanged(nameof(HasSelection), nameof(Requester), nameof(RequesterWorkstation),
                                    nameof(PreviousTickets), nameof(ResponderLabel));
            }
        }

        public bool HasSelection => Selected != null;
        public Employee Requester => _sim.RequesterOf(Selected);
        public Workstation RequesterWorkstation => _sim.WorkstationByTag(Selected?.WorkstationTag);

        public IEnumerable<Ticket> PreviousTickets => Requester == null
            ? Enumerable.Empty<Ticket>()
            : _sim.TicketsFor(Requester).Where(t => t != Selected).OrderByDescending(t => t.UpdatedUtc).Take(5);

        /// <summary>Names the responder so the conversation is never mistaken for a real person or a model.</summary>
        public string ResponderLabel => "SIMULATED REQUESTER \u00B7 " + _sim.Responder.DisplayName.ToUpperInvariant();

        public string Queue
        {
            get => _queue;
            set { if (Set(ref _queue, value)) Refresh(); }
        }

        public string Search
        {
            get => _search;
            set { if (Set(ref _search, value)) Refresh(); }
        }

        public string Reply
        {
            get => _reply;
            set => Set(ref _reply, value);
        }

        public int OpenCount => _sim.OpenTickets;
        public int UnassignedCount => _sim.UnassignedTickets;
        public int HighCount => _sim.HighPriorityTickets;
        public int CriticalCount => _sim.CriticalTickets;
        public int ResolvedCount => _sim.ResolvedTickets;

        public override void OnActivated() => Refresh();

        public override void OnTick() => _sim.RefreshTimeDependent();

        public void SelectPayload(object payload)
        {
            if (payload is not Ticket ticket) return;

            Queue = ticket.IsOpen ? "ALL" : "RESOLVED";
            Refresh();
            Selected = ticket;
        }

        private void Send()
        {
            var body = Reply;
            Reply = "";
            _sim.SendMessage(Selected, body, Technician);
        }

        private void Resolve()
        {
            if (Selected == null) return;

            if (!DialogService.Confirm("RESOLVE TICKET",
                    Selected.Reference + " will be marked resolved and the requester will be notified.", "RESOLVE"))
                return;

            _sim.SetStatus(Selected, TicketStatus.Resolved);
            AppServices.Toasts.Show("TICKET RESOLVED", Selected.Reference + " \u00B7 " + Selected.Title,
                                    ToastKind.Success);
            Refresh();
        }

        private void Refresh()
        {
            IEnumerable<Ticket> source = _sim.Tickets;

            source = Queue switch
            {
                "OPEN" => source.Where(t => t.IsOpen),
                "MY QUEUE" => source.Where(t => t.IsOpen &&
                                  string.Equals(t.AssignedTo, Technician, StringComparison.OrdinalIgnoreCase)),
                "UNASSIGNED" => source.Where(t => t.IsOpen && !t.IsAssigned),
                "IN PROGRESS" => source.Where(t => t.Status == TicketStatus.InProgress),
                "WAITING" => source.Where(t => t.Status == TicketStatus.WaitingForUser),
                "CRITICAL" => source.Where(t => t.IsOpen && t.Priority >= TicketPriority.High),
                "RESOLVED" => source.Where(t => t.IsResolved),
                _ => source
            };

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var q = Search.Trim();
                source = source.Where(t =>
                    (t.Reference ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (t.Title ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (t.RequesterUsername ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (t.WorkstationTag ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            Visible.Clear();
            foreach (var t in source
                         .OrderByDescending(t => t.IsOpen)
                         .ThenByDescending(t => (int)t.Priority)
                         .ThenByDescending(t => t.UpdatedUtc))
                Visible.Add(t);

            OnPropertiesChanged(nameof(OpenCount), nameof(UnassignedCount), nameof(HighCount),
                                nameof(CriticalCount), nameof(ResolvedCount));
        }
    }
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using NEXUS.Common;
using NEXUS.Models;
using NEXUS.Services;

namespace NEXUS.ViewModels
{
    /// <summary>
    /// Root view-model. Owns navigation across every NEXUS subsystem, the one-second
    /// clock that drives live telemetry, the global command palette, and the global
    /// commands exposed through the sidebar and hotkeys.
    /// </summary>
    public class ShellViewModel : ViewModelBase
    {
        private readonly DispatcherTimer _clock;
        private NavSection _current;
        private readonly DateTime _startedAt = DateTime.Now;
        private int _tick;

        public ShellViewModel()
        {
            Commands = new TaskCommands(AppServices.Tasks, AppServices.Toasts);

            // --- operations subsystems ---
            CommandCenter = new CommandCenterViewModel(this);
            DevicesSection = new DevicesViewModel();
            NetworkSection = new NetworkViewModel(this);
            ServersSection = new ServersViewModel();
            SecuritySection = new SecurityViewModel();
            UsersSection = new UsersViewModel(this);
            AccessSection = new AccessViewModel();
            AutomationsSection = new AutomationsViewModel();
            TerminalSection = new TerminalViewModel(this);
            LogsSection = new LogsViewModel();
            SystemSection = new SystemViewModel();
            ServiceDeskSection = new ServiceDeskViewModel(this);
            DirectorySection = new DirectoryViewModel(this);
            FilesSection = new FilesViewModel(this);
            DocumentsSection = new DocumentsViewModel(this);

            // --- task subsystem (unchanged) ---
            Overview = new OverviewViewModel(AppServices.Tasks, AppServices.Activity, Commands, this);
            TasksSection = new TasksViewModel(AppServices.Tasks, Commands);
            FocusSection = new FocusViewModel(AppServices.Focus, AppServices.Tasks, AppServices.Toasts, AppServices.Store);
            AnalyticsSection = new AnalyticsViewModel(AppServices.Tasks, AppServices.Store);
            ActivitySection = new ActivityViewModel(AppServices.Activity);
            SettingsSection = new SettingsViewModel(AppServices.Store, AppServices.Theme, AppServices.Tasks,
                                                    AppServices.Toasts, AppServices.Activity);

            NewTaskCommand = new RelayCommand(_ => NewTask());
            StartFocusCommand = new RelayCommand(_ => StartFocus(null));
            ShowTasksCommand = new RelayCommand(p => ShowTasks(p?.ToString() ?? "ALL"));
            ShowSettingsCommand = new RelayCommand(_ => Navigate("SETTINGS"));
            NavigateCommand = new RelayCommand(p => Navigate(p?.ToString()));
            DismissToastCommand = new RelayCommand(p => { if (p is Toast t) AppServices.Toasts.Dismiss(t); });
            SweepCommand = new RelayCommand(_ => RunSweep());
            OpenSearchCommand = new RelayCommand(_ => Search.Open());

            QuickSection = new QuickActionsViewModel(this);
            Search = new SearchViewModel(this);

            BuildSections();

            Commands.FocusRequested = StartFocus;
            AppServices.Tasks.Changed += OnTasksChanged;
            AppServices.Tasks.TaskCompleted += OnTaskCompleted;
            AppServices.Focus.SessionFinished += OnFocusFinished;
            AppServices.Bus.AlertsChanged += RefreshBadges;
            AppServices.Bus.Published += OnEventPublished;

            RegisterPaletteCommands();

            var startKey = AppServices.Settings.RestoreLastSection ? AppServices.Settings.LastSection : "COMMAND";
            Navigate(Sections.Any(s => s.Key == startKey) ? startKey : "COMMAND");

            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += OnClockTick;
            _clock.Start();
            OnClockTick(null, EventArgs.Empty);
        }

        // ---------- sections ----------

        public ObservableCollection<NavSection> Sections { get; } = new ObservableCollection<NavSection>();
        public TaskCommands Commands { get; }
        public SearchViewModel Search { get; }

        public CommandCenterViewModel CommandCenter { get; }
        public DevicesViewModel DevicesSection { get; }
        public NetworkViewModel NetworkSection { get; }
        public ServersViewModel ServersSection { get; }
        public SecurityViewModel SecuritySection { get; }
        public UsersViewModel UsersSection { get; }
        public AccessViewModel AccessSection { get; }
        public AutomationsViewModel AutomationsSection { get; }
        public TerminalViewModel TerminalSection { get; }
        public LogsViewModel LogsSection { get; }
        public SystemViewModel SystemSection { get; }
        public ServiceDeskViewModel ServiceDeskSection { get; }
        public DirectoryViewModel DirectorySection { get; }
        public FilesViewModel FilesSection { get; }
        public DocumentsViewModel DocumentsSection { get; }

        public OverviewViewModel Overview { get; }
        public TasksViewModel TasksSection { get; }
        public QuickActionsViewModel QuickSection { get; }
        public FocusViewModel FocusSection { get; }
        public AnalyticsViewModel AnalyticsSection { get; }
        public ActivityViewModel ActivitySection { get; }
        public SettingsViewModel SettingsSection { get; }

        public ObservableCollection<Toast> Toasts => AppServices.Toasts.Items;

        private void BuildSections()
        {
            void Add(string key, string label, string glyph, ViewModelBase vm, string group)
                => Sections.Add(new NavSection { Key = key, Label = label, Glyph = glyph, ViewModel = vm, Group = group });

            Sections.Add(NavSection.Header("OPERATIONS"));
            Add("COMMAND", "COMMAND CENTER", "Command", CommandCenter, "OPERATIONS");
            Add("SYSTEM", "SYSTEM", "System", SystemSection, "OPERATIONS");
            Add("TERMINAL", "TERMINAL", "Terminal", TerminalSection, "OPERATIONS");

            Sections.Add(NavSection.Header("SERVICE DESK"));
            Add("SERVICEDESK", "TICKETS", "Tasks", ServiceDeskSection, "SERVICE DESK");
            Add("DIRECTORY", "DIRECTORY", "Users", DirectorySection, "SERVICE DESK");

            Sections.Add(NavSection.Header("INFRASTRUCTURE"));
            Add("DEVICES", "DEVICES", "Devices", DevicesSection, "INFRASTRUCTURE");
            Add("NETWORK", "NETWORK", "Network", NetworkSection, "INFRASTRUCTURE");
            Add("SERVERS", "SERVERS & SERVICES", "Server", ServersSection, "INFRASTRUCTURE");
            Add("AUTOMATIONS", "AUTOMATIONS", "Automation", AutomationsSection, "INFRASTRUCTURE");

            Sections.Add(NavSection.Header("ACCESS & SECURITY"));
            Add("SECURITY", "SECURITY", "Security", SecuritySection, "ACCESS");
            Add("USERS", "USERS", "Users", UsersSection, "ACCESS");
            Add("ACCESS", "ACCESS CONTROL", "Access", AccessSection, "ACCESS");
            Add("LOGS", "AUDIT LOG", "Log", LogsSection, "ACCESS");

            Sections.Add(NavSection.Header("WORKSPACE"));
            Add("DOCUMENTS", "DOCUMENTS", "Documents", DocumentsSection, "WORKSPACE");
            Add("FILES", "FILES", "Files", FilesSection, "WORKSPACE");

            Sections.Add(NavSection.Header("WORK"));
            Add("OVERVIEW", "MISSION OVERVIEW", "Overview", Overview, "WORK");
            Add("TASKS", "TASKS", "Tasks", TasksSection, "WORK");
            Add("FOCUS", "FOCUS", "Focus", FocusSection, "WORK");
            Add("ANALYTICS", "ANALYTICS", "Analytics", AnalyticsSection, "WORK");
            Add("QUICK", "QUICK ACTIONS", "Quick", QuickSection, "WORK");
            Add("ACTIVITY", "TASK ACTIVITY", "Activity", ActivitySection, "WORK");

            Sections.Add(NavSection.Header("CONFIGURATION"));
            Add("SETTINGS", "SETTINGS", "Settings", SettingsSection, "CONFIGURATION");
        }

        public NavSection Current
        {
            get => _current;
            private set
            {
                if (_current != null) _current.IsSelected = false;
                Set(ref _current, value);
                if (_current != null) _current.IsSelected = true;
                OnPropertiesChanged(nameof(CurrentViewModel), nameof(CurrentTitle), nameof(CurrentSubtitle));
            }
        }

        public ViewModelBase CurrentViewModel => Current?.ViewModel;
        public string CurrentTitle => Current?.Label ?? "";

        public string CurrentSubtitle => Current?.Key switch
        {
            "COMMAND" => "Live state of every connected subsystem",
            "SYSTEM" => "Telemetry and controls for this machine",
            "TERMINAL" => "Administrative command console",
            "DEVICES" => "Inventory, status and device controls",
            "NETWORK" => "Topology, interfaces and reachability",
            "SERVERS" => "Server health and service control",
            "AUTOMATIONS" => "Event-driven rule engine",
            "SECURITY" => "Posture, sessions and the audit trail",
            "USERS" => "Account directory and session management",
            "ACCESS" => "Permission matrix and policy control",
            "LOGS" => "Chronological record of every subsystem event",
            "SERVICEDESK" => "Ticket queue and incident console for " + Services.SimulationEngine.CompanyName,
            "DIRECTORY" => "Employee directory and account administration",
            "DOCUMENTS" => "Author, organise and revise operational documents",
            "FILES" => "File library, vault storage and organisation",
            "OVERVIEW" => "Mission state for the task subsystem",
            "TASKS" => "Full task register with search, filters and sorting",
            "FOCUS" => "Timed execution sessions",
            "ANALYTICS" => "Throughput, distribution and completion history",
            "QUICK" => "One-click operations",
            "ACTIVITY" => "Task subsystem activity",
            "SETTINGS" => "Appearance, behaviour and data management",
            _ => ""
        };

        // ---------- commands ----------

        public ICommand NewTaskCommand { get; }
        public ICommand StartFocusCommand { get; }
        public ICommand ShowTasksCommand { get; }
        public ICommand ShowSettingsCommand { get; }
        public ICommand NavigateCommand { get; }
        public ICommand DismissToastCommand { get; }
        public ICommand SweepCommand { get; }
        public ICommand OpenSearchCommand { get; }

        // ---------- live header ----------

        public string TimeDisplay { get; private set; } = "";
        public string SecondsDisplay { get; private set; } = "";
        public string DateDisplay { get; private set; } = "";
        public string DayDisplay { get; private set; } = "";
        public string UptimeDisplay { get; private set; } = "";

        public string SystemStatus => "OPERATIONAL";
        public string OperatorName => AppServices.Users.Current?.DisplayName ?? "UNIDENTIFIED";
        public string OperatorRole => AppServices.Users.Current?.RoleLabel ?? "\u2014";
        public string OperatorInitials => AppServices.Users.Current?.Initials ?? "??";

        public int AlertCount => AppServices.Bus.UnacknowledgedAlerts;
        public bool HasAlerts => AlertCount > 0;
        public int DeviceOnline => AppServices.Devices.OnlineCount;
        public int DeviceTotal => AppServices.Devices.Total;
        public int ServicesRunning => AppServices.Servers.RunningServices;
        public string NetworkStatus => AppServices.Network.StatusLabel;
        public string NetworkBrushKey => AppServices.Network.StatusBrushKey;
        public string SecurityPosture => AppServices.Security.PostureLabel;
        public string SecurityBrushKey => AppServices.Security.PostureBrushKey;
        public string CpuPercent => AppServices.SystemInfo.CpuPercent;
        public string MemoryPercent => AppServices.SystemInfo.MemoryPercent;

        // ---------- deadline (task subsystem) ----------

        public string DeadlineDays { get; private set; } = "0";
        public string DeadlineHours { get; private set; } = "00";
        public string DeadlineMinutes { get; private set; } = "00";
        public string DeadlineSeconds { get; private set; } = "00";
        public string DeadlineDayName { get; private set; } = "WEDNESDAY";
        public string DeadlineDateText { get; private set; } = "";
        public bool IsDeadlinePassed { get; private set; }
        public string DeadlineStatusText { get; private set; } = "";
        public double DeadlineFraction { get; private set; }
        public string DeadlineBrushKey { get; private set; } = "BrushAccent";
        public string CompactCountdown { get; private set; } = "";

        public int HeaderRemaining => AppServices.Tasks.Remaining;
        public int HeaderCompleted => AppServices.Tasks.Completed;
        public int HeaderPercent => AppServices.Tasks.CompletionPercent;

        // ---------- behaviour ----------

        public void Navigate(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            var section = Sections.FirstOrDefault(s => !s.IsHeader && s.Key == key);
            if (section == null || section == Current) return;

            Current = section;
            section.ViewModel?.OnActivated();

            AppServices.Settings.LastSection = key;
            AppServices.Store.RequestSave();
        }

        /// <summary>Opens the devices module focused on one device.</summary>
        public void OpenDevice(Device device)
        {
            Navigate("DEVICES");
            DevicesSection.SelectPayload(device);
        }

        /// <summary>Opens the access matrix with a user pre-selected in the directory.</summary>
        public void OpenAccessFor(NexusUser user)
        {
            if (user != null) UsersSection.SelectPayload(user);
            Navigate("ACCESS");
        }

        /// <summary>Routes a chosen palette result into the owning module.</summary>
        public void OpenSearchResult(SearchResult result)
        {
            if (result == null) return;

            Navigate(result.SectionKey);

            switch (result.SectionKey)
            {
                case "DEVICES": DevicesSection.SelectPayload(result.Payload); break;
                case "USERS": UsersSection.SelectPayload(result.Payload); break;
                case "SERVERS": ServersSection.SelectPayload(result.Payload); break;
                case "SERVICEDESK": ServiceDeskSection.SelectPayload(result.Payload); break;
                case "DIRECTORY": DirectorySection.SelectPayload(result.Payload); break;
                case "FILES": FilesSection.SelectPayload(result.Payload); break;
                case "DOCUMENTS": DocumentsSection.SelectPayload(result.Payload); break;
            }
        }

        /// <summary>Opens the service desk on a specific ticket.</summary>
        public void OpenTicket(Models.Ticket ticket)
        {
            Navigate("SERVICEDESK");
            ServiceDeskSection.SelectPayload(ticket);
        }

        /// <summary>Opens the directory on a specific employee.</summary>
        public void OpenEmployee(Models.Employee employee)
        {
            Navigate("DIRECTORY");
            DirectorySection.SelectPayload(employee);
        }

        /// <summary>Opens the document workspace on a specific document.</summary>
        public void OpenDocument(Models.NexusFile document)
        {
            Navigate("DOCUMENTS");
            DocumentsSection.SelectPayload(document);
        }

        /// <summary>
        /// Registers the verb-style entries of the command palette. These run an action
        /// rather than navigating, so "Restart Jellyfin" actually restarts the service.
        /// </summary>
        private void RegisterPaletteCommands()
        {
            var search = AppServices.Search;

            search.RegisterCommand("Run system check", "Poll every subsystem now", "Refresh", RunSweep);
            search.RegisterCommand("Create task", "Open the new task editor", "Add",
                () => NewTaskCommand.Execute(null));
            search.RegisterCommand("Upload files", "Add files to the NEXUS vault", "Upload",
                () => { Navigate("FILES"); FilesSection.UploadCommand.Execute(null); });
            search.RegisterCommand("New document", "Start a new NEXUS document", "Document",
                () => { Navigate("DOCUMENTS"); DocumentsSection.NewCommand.Execute(null); });
            search.RegisterCommand("Acknowledge all alerts", "Clear the outstanding alert queue", "Check",
                () => AppServices.Bus.AcknowledgeAll());
            search.RegisterCommand("Discover network neighbours", "Read this machine's ARP cache", "Network",
                () => { Navigate("NETWORK"); AppServices.Network.DiscoverFromArpTable(); });
            search.RegisterCommand("Open service desk", "Ticket queue and incident console", "Tasks",
                () => Navigate("SERVICEDESK"));
            search.RegisterCommand("Open directory", "Employee directory and accounts", "Users",
                () => Navigate("DIRECTORY"));
            search.RegisterCommand("Open terminal", "Administrative command console", "Terminal",
                () => Navigate("TERMINAL"));
            search.RegisterCommand("View security events", "Posture, sessions and the audit trail", "Security",
                () => Navigate("SECURITY"));

            // One entry per service, so services can be driven straight from the palette.
            foreach (var server in AppServices.Servers.Servers)
                foreach (var service in server.Services)
                {
                    var target = service;
                    var host = server;
                    search.RegisterCommand("Restart " + target.Name,
                        "Restart this service on " + host.Name, "Restart",
                        () =>
                        {
                            AppServices.Servers.RestartService(target);
                            Navigate("SERVERS");
                            ServersSection.SelectPayload(target);
                        });
                    search.RegisterCommand("Stop " + target.Name,
                        "Stop this service on " + host.Name, "Stop",
                        () =>
                        {
                            AppServices.Servers.StopService(target);
                            Navigate("SERVERS");
                            ServersSection.SelectPayload(target);
                        });
                }

            // One entry per device, so a device console is always one search away.
            foreach (var device in AppServices.Devices.Devices)
            {
                var target = device;
                search.RegisterCommand("Open " + target.Name,
                    target.TypeLabel + " control console", target.Glyph, () => OpenDevice(target));
            }
        }

        /// <summary>Operator-initiated poll of every subsystem.</summary>
        public void RunSweep()
        {
            AppServices.SystemInfo.Sample();
            AppServices.Network.Refresh(quiet: false);
            AppServices.Devices.RunHealthCheck();
            AppServices.Servers.RefreshMetrics();

            AppServices.Bus.Publish(EventCategory.System, "System check complete",
                AppServices.Devices.OnlineCount + " devices online \u00B7 "
                + AppServices.Servers.RunningServices + " services running",
                EventSeverity.Notice, "CORE");

            AppServices.Toasts.Show("SYSTEM CHECK COMPLETE", "All subsystems polled.", ToastKind.Success);
            RefreshBadges();
        }

        private void NewTask()
        {
            var seed = new MissionTask
            {
                Priority = TaskPriority.Normal,
                State = TaskState.Planned,
                Deadline = AppServices.Settings.MissionDeadline
            };

            var created = DialogService.EditTask(seed, "NEW TASK");
            if (created == null) return;

            AppServices.Tasks.Add(created);
            AppServices.Toasts.Show("TASK CREATED", created.Title, ToastKind.Success);
            if (Current?.Key != "TASKS") Navigate("TASKS");
        }

        private void StartFocus(MissionTask task)
        {
            Navigate("FOCUS");
            FocusSection.StartFor(task);
        }

        private void ShowTasks(string preset)
        {
            Navigate("TASKS");
            TasksSection.ApplyPreset(preset);
        }

        private void OnTasksChanged()
            => OnPropertiesChanged(nameof(HeaderRemaining), nameof(HeaderCompleted), nameof(HeaderPercent));

        private void OnTaskCompleted(MissionTask task)
        {
            if (AppServices.Settings.SoundEnabled) SystemService.PlayChime();
            AppServices.Toasts.Show("TASK COMPLETED", task.Title, ToastKind.Success);
        }

        private void OnFocusFinished(MissionTask task)
        {
            if (AppServices.Settings.SoundEnabled) SystemService.PlayAlert();
            if (AppServices.Settings.NotificationsEnabled)
                AppServices.Toasts.Show("FOCUS SESSION COMPLETE",
                    task != null ? task.Title : "Session finished", ToastKind.Success, 8);

            FocusSection.OnSessionFinished();
            FocusFinished?.Invoke();
        }

        private void OnEventPublished(SystemEvent e)
        {
            if (e.Severity == EventSeverity.Critical && AppServices.Settings.ToastsEnabled)
                AppServices.Toasts.Show(e.CategoryLabel, e.Message, ToastKind.Critical, 6);

            RefreshBadges();
        }

        private void RefreshBadges()
        {
            var security = Sections.FirstOrDefault(s => s.Key == "SECURITY");
            if (security != null)
                security.Badge = AlertCount > 0 ? AlertCount.ToString() : "";

            var desk = Sections.FirstOrDefault(s => s.Key == "SERVICEDESK");
            if (desk != null)
                desk.Badge = AppServices.Simulation.OpenTickets > 0
                    ? AppServices.Simulation.OpenTickets.ToString() : "";

            var files = Sections.FirstOrDefault(s => s.Key == "FILES");
            if (files != null)
                files.Badge = AppServices.Files.PinnedCount > 0
                    ? AppServices.Files.PinnedCount.ToString() : "";

            var devices = Sections.FirstOrDefault(s => s.Key == "DEVICES");
            if (devices != null)
                devices.Badge = AppServices.Devices.OfflineCount > 0
                    ? AppServices.Devices.OfflineCount.ToString() : "";

            OnPropertiesChanged(nameof(AlertCount), nameof(HasAlerts), nameof(SecurityPosture),
                                nameof(SecurityBrushKey), nameof(DeviceOnline), nameof(DeviceTotal),
                                nameof(ServicesRunning));
        }

        /// <summary>Raised so the window can pull itself forward.</summary>
        public event Action FocusFinished;

        private void OnClockTick(object sender, EventArgs e)
        {
            var now = DateTime.Now;
            _tick++;

            TimeDisplay = now.ToString("HH:mm");
            SecondsDisplay = now.ToString("ss");
            DateDisplay = now.ToString("dd MMM yyyy").ToUpperInvariant();
            DayDisplay = now.ToString("dddd").ToUpperInvariant();

            var up = now - _startedAt;
            UptimeDisplay = ((int)up.TotalHours).ToString("00") + ":" + up.Minutes.ToString("00") + ":" + up.Seconds.ToString("00");

            UpdateDeadline(now);

            // Telemetry is sampled every other second; the network stack every ten.
            if (_tick % 2 == 0) AppServices.SystemInfo.Sample();
            if (_tick % 10 == 0)
            {
                AppServices.Network.Refresh();
                AppServices.Network.MeasureLatency();
                AppServices.Servers.RefreshMetrics();
                RefreshBadges();
            }

            OnPropertiesChanged(nameof(TimeDisplay), nameof(SecondsDisplay), nameof(DateDisplay),
                nameof(DayDisplay), nameof(UptimeDisplay), nameof(CpuPercent), nameof(MemoryPercent),
                nameof(NetworkStatus), nameof(NetworkBrushKey),
                nameof(DeadlineDays), nameof(DeadlineHours), nameof(DeadlineMinutes), nameof(DeadlineSeconds),
                nameof(DeadlineDayName), nameof(DeadlineDateText), nameof(IsDeadlinePassed),
                nameof(DeadlineStatusText), nameof(DeadlineFraction), nameof(DeadlineBrushKey),
                nameof(CompactCountdown));

            AppServices.Tasks.RefreshTimeDependent();
            if (_tick % 5 == 0) AppServices.Simulation.RefreshTimeDependent();
            CurrentViewModel?.OnTick();
        }

        private void UpdateDeadline(DateTime now)
        {
            var deadline = AppServices.Settings.MissionDeadline;
            DeadlineDayName = deadline.ToString("dddd").ToUpperInvariant();
            DeadlineDateText = deadline.ToString("dd MMM \u00B7 HH:mm").ToUpperInvariant();

            var remaining = deadline - now;
            IsDeadlinePassed = remaining.TotalSeconds <= 0;

            if (IsDeadlinePassed)
            {
                var over = now - deadline;
                DeadlineDays = ((int)over.TotalDays).ToString();
                DeadlineHours = over.Hours.ToString("00");
                DeadlineMinutes = over.Minutes.ToString("00");
                DeadlineSeconds = over.Seconds.ToString("00");
                DeadlineStatusText = "DEADLINE PASSED";
                DeadlineBrushKey = "BrushCritical";
                DeadlineFraction = 1;
                CompactCountdown = "OVERDUE BY " + DeadlineDays + "D " + DeadlineHours + "H";
                return;
            }

            DeadlineDays = ((int)remaining.TotalDays).ToString();
            DeadlineHours = remaining.Hours.ToString("00");
            DeadlineMinutes = remaining.Minutes.ToString("00");
            DeadlineSeconds = remaining.Seconds.ToString("00");
            DeadlineStatusText = "TIME REMAINING";

            DeadlineBrushKey = remaining.TotalHours < 12 ? "BrushCritical"
                : remaining.TotalHours < 36 ? "BrushHigh"
                : remaining.TotalHours < 72 ? "BrushNormal" : "BrushCompleted";

            var window = TimeSpan.FromDays(7).TotalSeconds;
            DeadlineFraction = Math.Max(0, Math.Min(1, 1 - remaining.TotalSeconds / window));
            CompactCountdown = DeadlineDays + "D " + DeadlineHours + "H " + DeadlineMinutes + "M";
        }

        public void Shutdown()
        {
            _clock.Stop();
            AppServices.Bus.Publish(EventCategory.System, "NEXUS core shutting down",
                "Session ended by operator", EventSeverity.Notice, "CORE");
            AppServices.Store.SaveNow();
        }
    }
}

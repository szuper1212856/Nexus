using System;
using NEXUS.Models;
using NEXUS.Services.Integrations;

namespace NEXUS.Services
{
    /// <summary>
    /// Composition root for NEXUS CORE. Construction order matters: the store loads state,
    /// the event bus opens the audit trail, then each manager attaches to it.
    ///
    ///   DataStore
    ///     +-- EventBus --+-- DeviceManager -- IntegrationManager
    ///                    +-- NetworkManager
    ///                    +-- UserManager -- AccessManager
    ///                    +-- ServerManager -- SystemInfoService
    ///                    +-- SecurityManager
    ///                    +-- AutomationEngine
    ///                    +-- TerminalService
    ///                    +-- SearchService
    /// </summary>
    public static class AppServices
    {
        // --- task subsystem (original) ---
        public static DataStore Store { get; private set; }
        public static ActivityService Activity { get; private set; }
        public static TaskService Tasks { get; private set; }
        public static FocusService Focus { get; private set; }
        public static ToastService Toasts { get; private set; }
        public static ThemeService Theme { get; private set; }

        // --- operations subsystems ---
        public static EventBus Bus { get; private set; }
        public static IntegrationManager Integrations { get; private set; }
        public static DeviceManager Devices { get; private set; }
        public static NetworkManager Network { get; private set; }
        public static UserManager Users { get; private set; }
        public static AccessManager Access { get; private set; }
        public static SystemInfoService SystemInfo { get; private set; }
        public static ServerManager Servers { get; private set; }
        public static SecurityManager Security { get; private set; }
        public static AutomationEngine Automations { get; private set; }
        public static TerminalService Terminal { get; private set; }
        public static SearchService Search { get; private set; }
        public static FileVault Files { get; private set; }
        public static SimulationEngine Simulation { get; private set; }

        public static AppSettings Settings => Store.Data.Settings;

        public static void Initialize()
        {
            Store = new DataStore();
            Store.Load();

            // Task subsystem, unchanged.
            Activity = new ActivityService(Store);
            Tasks = new TaskService(Store, Activity);
            Focus = new FocusService(Store, Activity);
            Toasts = new ToastService(Store);
            Theme = new ThemeService();
            Theme.Apply(Settings.Theme, Settings.Accent);

            // Operations subsystems.
            Bus = new EventBus(Store);
            Integrations = new IntegrationManager();
            Devices = new DeviceManager(Store, Bus, Integrations);
            Network = new NetworkManager(Store, Bus, Devices);
            Users = new UserManager(Store, Bus);
            Access = new AccessManager(Store, Bus, Users);
            SystemInfo = new SystemInfoService();
            Servers = new ServerManager(Store, Bus, SystemInfo);
            Security = new SecurityManager(Bus, Users);
            Automations = new AutomationEngine(Store, Bus, Users, Servers, Devices, Toasts);
            Terminal = new TerminalService(Devices, Network, Users, Access, Servers,
                                           Security, Automations, SystemInfo, Tasks, Bus);
            Files = new FileVault(Store, Bus);
            Simulation = new SimulationEngine(Store, Bus);
            Search = new SearchService(Devices, Users, Servers, Tasks, Automations, Files, Simulation, Bus);

            SystemInfo.Sample();
            Network.Refresh();

            SeedIfEmpty();
            BridgeTaskEvents();

            Users.SignInPrimary();

            Bus.Publish(EventCategory.System, "NEXUS core online",
                "All subsystems initialised on " + Environment.MachineName,
                EventSeverity.Notice, "CORE");
        }

        /// <summary>Provisions each subsystem the first time it is seen, including on upgrade from v1 data.</summary>
        private static void SeedIfEmpty()
        {
            if (Store.IsFirstRun && Tasks.Total == 0) Tasks.SeedInitialTasks();
            if (Devices.Total == 0) Devices.SeedDefaults();
            if (Users.Users.Count == 0) Users.SeedDefaults(OperatorName());
            if (Servers.Servers.Count == 0) Servers.SeedDefaults();
            if (Automations.Rules.Count == 0) Automations.SeedDefaults();
            if (Files.Folders.Count == 0) Files.SeedDefaults();
            if (Simulation.Employees.Count == 0) Simulation.SeedCompany();

            Store.Data.SchemaVersion = 4;
        }

        private static string OperatorName()
        {
            var name = Environment.UserName;
            if (string.IsNullOrWhiteSpace(name)) return "Operator";
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>
        /// Mirrors task-subsystem activity into the central audit trail so the task
        /// module behaves like every other NEXUS subsystem, without touching its code.
        /// </summary>
        private static void BridgeTaskEvents()
        {
            Tasks.TaskCompleted += task =>
            {
                Bus.Publish(EventCategory.Task, "Task completed", task.Title, EventSeverity.Info, "TASKS");
                Bus.Signal(TriggerType.TaskCompleted, task.Title, task.PriorityLabel, task);
            };

            Automations.TaskRequested += (rule, signal) =>
            {
                Tasks.Add(new MissionTask
                {
                    Title = string.IsNullOrWhiteSpace(rule.ActionParameter)
                        ? rule.Name + ": " + signal.Subject
                        : rule.ActionParameter,
                    Priority = TaskPriority.High,
                    State = TaskState.Planned,
                    Notes = "Raised automatically by the automation \"" + rule.Name + "\".",
                    Deadline = Settings.MissionDeadline
                });
            };
        }

        /// <summary>Rebuilds runtime state after an import or a data reset.</summary>
        public static void RebindAfterDataChange()
        {
            Activity.Reload();
            Bus.Reload();
            Theme.Apply(Settings.Theme, Settings.Accent);
            Tasks.Touch();
            Devices.Touch();
            Users.Touch();
            Servers.Touch();
            Automations.Touch();
            Files.Touch();
            Simulation.Touch();
        }
    }
}

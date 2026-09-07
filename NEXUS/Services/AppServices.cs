namespace NEXUS.Services
{
    /// <summary>
    /// Small composition root. The app is single-window and single-user, so a static
    /// locator is simpler than a container while keeping construction order explicit.
    /// </summary>
    public static class AppServices
    {
        public static DataStore Store { get; private set; }
        public static ActivityService Activity { get; private set; }
        public static TaskService Tasks { get; private set; }
        public static FocusService Focus { get; private set; }
        public static ToastService Toasts { get; private set; }
        public static ThemeService Theme { get; private set; }

        public static Models.AppSettings Settings => Store.Data.Settings;

        public static void Initialize()
        {
            Store = new DataStore();
            Store.Load();

            Activity = new ActivityService(Store);
            Tasks = new TaskService(Store, Activity);
            Focus = new FocusService(Store, Activity);
            Toasts = new ToastService(Store);
            Theme = new ThemeService();

            Theme.Apply(Settings.Theme, Settings.Accent);

            if (Store.IsFirstRun && Tasks.Total == 0)
                Tasks.SeedInitialTasks();
        }

        /// <summary>Rebuilds runtime state after an import or a data reset.</summary>
        public static void RebindAfterDataChange()
        {
            Activity.Reload();
            Theme.Apply(Settings.Theme, Settings.Accent);
            Tasks.Touch();
        }
    }
}

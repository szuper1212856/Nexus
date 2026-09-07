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
    /// Root view-model. Owns navigation, the one-second clock that drives every live
    /// value in the UI, and the global commands exposed through the sidebar and hotkeys.
    /// </summary>
    public class ShellViewModel : ViewModelBase
    {
        private readonly DispatcherTimer _clock;
        private NavSection _current;
        private DateTime _startedAt = DateTime.Now;

        public ShellViewModel()
        {
            Commands = new TaskCommands(AppServices.Tasks, AppServices.Toasts);

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

            // Quick actions needs the commands above, so it is built last.
            QuickSection = new QuickActionsViewModel(this);

            Sections = new ObservableCollection<NavSection>
            {
                new NavSection { Key = "OVERVIEW",  Label = "OVERVIEW",      Glyph = "\uE9D9", ViewModel = Overview },
                new NavSection { Key = "TASKS",     Label = "TASKS",         Glyph = "\uE8FD", ViewModel = TasksSection },
                new NavSection { Key = "QUICK",     Label = "QUICK ACTIONS", Glyph = "\uE945", ViewModel = QuickSection },
                new NavSection { Key = "FOCUS",     Label = "FOCUS",         Glyph = "\uE916", ViewModel = FocusSection },
                new NavSection { Key = "ANALYTICS", Label = "ANALYTICS",     Glyph = "\uE9D2", ViewModel = AnalyticsSection },
                new NavSection { Key = "ACTIVITY",  Label = "ACTIVITY LOG",  Glyph = "\uE81C", ViewModel = ActivitySection },
                new NavSection { Key = "SETTINGS",  Label = "SETTINGS",      Glyph = "\uE713", ViewModel = SettingsSection }
            };

            Commands.FocusRequested = StartFocus;
            AppServices.Tasks.Changed += OnTasksChanged;
            AppServices.Tasks.TaskCompleted += OnTaskCompleted;
            AppServices.Focus.SessionFinished += OnFocusFinished;

            var startKey = AppServices.Settings.RestoreLastSection ? AppServices.Settings.LastSection : "OVERVIEW";
            Navigate(Sections.Any(s => s.Key == startKey) ? startKey : "OVERVIEW");

            _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clock.Tick += OnClockTick;
            _clock.Start();
            OnClockTick(null, EventArgs.Empty);
        }

        // ---------- sections ----------

        public ObservableCollection<NavSection> Sections { get; }
        public TaskCommands Commands { get; }
        public OverviewViewModel Overview { get; }
        public TasksViewModel TasksSection { get; }
        public QuickActionsViewModel QuickSection { get; }
        public FocusViewModel FocusSection { get; }
        public AnalyticsViewModel AnalyticsSection { get; }
        public ActivityViewModel ActivitySection { get; }
        public SettingsViewModel SettingsSection { get; }

        public ObservableCollection<Toast> Toasts => AppServices.Toasts.Items;

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
            "OVERVIEW" => "Live mission state and system telemetry",
            "TASKS" => "Full task register with search, filters and sorting",
            "QUICK" => "One-click operations on the current mission",
            "FOCUS" => "Timed execution sessions",
            "ANALYTICS" => "Throughput, distribution and completion history",
            "ACTIVITY" => "Chronological record of every operation",
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

        // ---------- live clock ----------

        public string TimeDisplay { get; private set; } = "";
        public string SecondsDisplay { get; private set; } = "";
        public string DateDisplay { get; private set; } = "";
        public string DayDisplay { get; private set; } = "";
        public string UptimeDisplay { get; private set; } = "";

        public string SystemStatus => "OPERATIONAL";

        // ---------- deadline ----------

        public string DeadlineDays { get; private set; } = "0";
        public string DeadlineHours { get; private set; } = "00";
        public string DeadlineMinutes { get; private set; } = "00";
        public string DeadlineSeconds { get; private set; } = "00";
        public string DeadlineDayName { get; private set; } = "WEDNESDAY";
        public string DeadlineDateText { get; private set; } = "";
        public bool IsDeadlinePassed { get; private set; }
        public string DeadlineStatusText { get; private set; } = "";
        public double DeadlineFraction { get; private set; }

        /// <summary>Colour key for the countdown: green early, amber inside a day, red once passed.</summary>
        public string DeadlineBrushKey { get; private set; } = "BrushAccent";

        public string CompactCountdown { get; private set; } = "";

        // ---------- behaviour ----------

        public void Navigate(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            var section = Sections.FirstOrDefault(s => s.Key == key);
            if (section == null || section == Current) return;

            Current = section;
            section.ViewModel?.OnActivated();

            AppServices.Settings.LastSection = key;
            AppServices.Store.RequestSave();
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

        public int HeaderRemaining => AppServices.Tasks.Remaining;
        public int HeaderCompleted => AppServices.Tasks.Completed;
        public int HeaderPercent => AppServices.Tasks.CompletionPercent;

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

        /// <summary>Raised so the window can flash itself into the foreground.</summary>
        public event Action FocusFinished;

        private void OnClockTick(object sender, EventArgs e)
        {
            var now = DateTime.Now;

            TimeDisplay = now.ToString("HH:mm");
            SecondsDisplay = now.ToString("ss");
            DateDisplay = now.ToString("dd MMM yyyy").ToUpperInvariant();
            DayDisplay = now.ToString("dddd").ToUpperInvariant();

            var up = now - _startedAt;
            UptimeDisplay = ((int)up.TotalHours).ToString("00") + ":" + up.Minutes.ToString("00") + ":" + up.Seconds.ToString("00");

            UpdateDeadline(now);

            OnPropertiesChanged(nameof(TimeDisplay), nameof(SecondsDisplay), nameof(DateDisplay),
                nameof(DayDisplay), nameof(UptimeDisplay),
                nameof(DeadlineDays), nameof(DeadlineHours), nameof(DeadlineMinutes), nameof(DeadlineSeconds),
                nameof(DeadlineDayName), nameof(DeadlineDateText), nameof(IsDeadlinePassed),
                nameof(DeadlineStatusText), nameof(DeadlineFraction), nameof(DeadlineBrushKey),
                nameof(CompactCountdown));

            // Overdue flags and relative deadline text change as time passes.
            AppServices.Tasks.RefreshTimeDependent();
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

            DeadlineBrushKey = remaining.TotalHours switch
            {
                < 12 => "BrushCritical",
                < 36 => "BrushHigh",
                < 72 => "BrushNormal",
                _ => "BrushCompleted"
            };

            // Window is capped at seven days so the bar stays meaningful.
            var window = TimeSpan.FromDays(7).TotalSeconds;
            DeadlineFraction = Math.Max(0, Math.Min(1, 1 - remaining.TotalSeconds / window));
            CompactCountdown = DeadlineDays + "D " + DeadlineHours + "H " + DeadlineMinutes + "M";
        }

        public void Shutdown()
        {
            _clock.Stop();
            AppServices.Store.SaveNow();
        }
    }
}

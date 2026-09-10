using System.Collections.ObjectModel;
using System.Linq;
using NEXUS.Models;
using NEXUS.Services;

namespace NEXUS.ViewModels
{
    /// <summary>Read-only aggregation of the mission state for the home screen.</summary>
    public class OverviewViewModel : ViewModelBase
    {
        private readonly TaskService _tasks;
        private readonly ActivityService _activity;

        public OverviewViewModel(TaskService tasks, ActivityService activity, TaskCommands commands, ShellViewModel shell)
        {
            _tasks = tasks;
            _activity = activity;
            Commands = commands;
            Shell = shell;

            _tasks.Changed += Recompute;
            _activity.Changed += RecomputeActivity;
            Recompute();
        }

        public TaskCommands Commands { get; }

        /// <summary>Exposed so the overview can show the live clock and countdown.</summary>
        public ShellViewModel Shell { get; }

        public ObservableCollection<MissionTask> ActiveTasks { get; } = new ObservableCollection<MissionTask>();
        public ObservableCollection<MissionTask> Segments { get; } = new ObservableCollection<MissionTask>();
        public ObservableCollection<ActivityEntry> RecentActivity { get; } = new ObservableCollection<ActivityEntry>();

        public int Total => _tasks.Total;
        public int Completed => _tasks.Completed;
        public int Remaining => _tasks.Remaining;
        public int Overdue => _tasks.Overdue;
        public int Blocked => _tasks.Blocked;
        public int InProgress => _tasks.InProgress;
        public double CompletionFraction => _tasks.CompletionFraction;
        public int CompletionPercent => _tasks.CompletionPercent;

        public string TotalLabel => Total == 1 ? "1 TASK" : Total + " TASKS";
        public bool HasTasks => Total > 0;
        public bool HasOverdue => Overdue > 0;

        public string HealthLabel
        {
            get
            {
                if (Total == 0) return "IDLE";
                if (Overdue > 0) return "ATTENTION";
                if (Blocked > 0) return "DEGRADED";
                if (CompletionPercent == 100) return "COMPLETE";
                return "NOMINAL";
            }
        }

        public string HealthBrushKey => HealthLabel switch
        {
            "ATTENTION" => "BrushCritical",
            "DEGRADED" => "BrushHigh",
            "COMPLETE" => "BrushCompleted",
            "IDLE" => "BrushTextMuted",
            _ => "BrushCompleted"
        };

        public override void OnActivated()
        {
            Recompute();
            RecomputeActivity();
        }

        private void Recompute()
        {
            ActiveTasks.Clear();
            foreach (var t in _tasks.Tasks.Where(t => !t.IsCompleted).OrderBy(t => t.SortWeight).Take(6))
                ActiveTasks.Add(t);

            Segments.Clear();
            // One block per task, capped so the bar stays readable on large missions.
            foreach (var t in _tasks.Tasks.OrderBy(t => t.IsCompleted ? 0 : 1).ThenBy(t => t.SortWeight).Take(30))
                Segments.Add(t);

            OnPropertiesChanged(nameof(Total), nameof(Completed), nameof(Remaining), nameof(Overdue),
                nameof(Blocked), nameof(InProgress), nameof(CompletionFraction), nameof(CompletionPercent),
                nameof(TotalLabel), nameof(HasTasks), nameof(HasOverdue), nameof(HealthLabel), nameof(HealthBrushKey));
        }

        private void RecomputeActivity()
        {
            RecentActivity.Clear();
            foreach (var e in _activity.Entries.Take(7)) RecentActivity.Add(e);
        }
    }
}

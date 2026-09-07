using System;
using System.Collections.ObjectModel;
using System.Linq;
using NEXUS.Common;
using NEXUS.Models;
using NEXUS.Services;

namespace NEXUS.ViewModels
{
    /// <summary>A single bar in one of the analytics charts.</summary>
    public class BarItem : ObservableObject
    {
        public string Label { get; set; }
        public string Sub { get; set; }
        public int Count { get; set; }
        public double Fraction { get; set; }
        public string BrushKey { get; set; } = "BrushAccent";
        public bool IsHighlighted { get; set; }
    }

    public class AnalyticsViewModel : ViewModelBase
    {
        private readonly TaskService _tasks;
        private readonly DataStore _store;

        public AnalyticsViewModel(TaskService tasks, DataStore store)
        {
            _tasks = tasks;
            _store = store;
            _tasks.Changed += Recompute;
            Recompute();
        }

        public ObservableCollection<BarItem> PriorityBars { get; } = new ObservableCollection<BarItem>();
        public ObservableCollection<BarItem> StatusBars { get; } = new ObservableCollection<BarItem>();
        public ObservableCollection<BarItem> HistoryBars { get; } = new ObservableCollection<BarItem>();

        public int Total => _tasks.Total;
        public int Completed => _tasks.Completed;
        public int Remaining => _tasks.Remaining;
        public int Overdue => _tasks.Overdue;
        public int CompletionPercent => _tasks.CompletionPercent;
        public double CompletionFraction => _tasks.CompletionFraction;

        public bool HasHistory => _store.Data.FocusSessions.Count > 0 || Completed > 0;

        /// <summary>Mean time from creation to completion. Needs at least two data points to be meaningful.</summary>
        public string AverageCompletionTime
        {
            get
            {
                var done = _tasks.Tasks.Where(t => t.IsCompleted && t.CompletedUtc.HasValue).ToList();
                if (done.Count < 2) return "INSUFFICIENT DATA";

                var avg = TimeSpan.FromTicks((long)done.Average(t => (t.CompletedUtc.Value - t.CreatedUtc).Ticks));
                if (avg.TotalDays >= 1) return Math.Round(avg.TotalDays, 1) + " DAYS";
                if (avg.TotalHours >= 1) return Math.Round(avg.TotalHours, 1) + " HOURS";
                return Math.Max(1, (int)avg.TotalMinutes) + " MIN";
            }
        }

        public string TotalFocusTime
        {
            get
            {
                var seconds = _store.Data.FocusSessions.Sum(s => s.ElapsedSeconds);
                var span = TimeSpan.FromSeconds(seconds);
                return (int)span.TotalHours + "h " + span.Minutes + "m";
            }
        }

        public int FocusSessionCount => _store.Data.FocusSessions.Count;

        public string BestDay
        {
            get
            {
                var groups = _tasks.Tasks
                    .Where(t => t.CompletedUtc.HasValue)
                    .GroupBy(t => t.CompletedUtc.Value.ToLocalTime().Date)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault();

                return groups == null
                    ? "\u2014"
                    : groups.Key.ToString("ddd dd MMM").ToUpperInvariant() + " \u00B7 " + groups.Count();
            }
        }

        public string Throughput
        {
            get
            {
                var last7 = _tasks.Tasks.Count(t => t.CompletedUtc.HasValue &&
                                                    t.CompletedUtc.Value.ToLocalTime().Date > DateTime.Today.AddDays(-7));
                return Math.Round(last7 / 7.0, 1) + " / DAY";
            }
        }

        public override void OnActivated() => Recompute();

        private void Recompute()
        {
            BuildPriority();
            BuildStatus();
            BuildHistory();

            OnPropertiesChanged(nameof(Total), nameof(Completed), nameof(Remaining), nameof(Overdue),
                nameof(CompletionPercent), nameof(CompletionFraction), nameof(AverageCompletionTime),
                nameof(TotalFocusTime), nameof(FocusSessionCount), nameof(BestDay), nameof(Throughput),
                nameof(HasHistory));
        }

        private void BuildPriority()
        {
            PriorityBars.Clear();
            var max = Math.Max(1, Enum.GetValues<TaskPriority>().Max(p => _tasks.CountByPriority(p)));

            foreach (var p in new[] { TaskPriority.Critical, TaskPriority.High, TaskPriority.Normal, TaskPriority.Low })
            {
                var count = _tasks.CountByPriority(p);
                PriorityBars.Add(new BarItem
                {
                    Label = p.ToString().ToUpperInvariant(),
                    Count = count,
                    Fraction = (double)count / max,
                    BrushKey = p switch
                    {
                        TaskPriority.Critical => "BrushCritical",
                        TaskPriority.High => "BrushHigh",
                        TaskPriority.Normal => "BrushNormal",
                        _ => "BrushLow"
                    }
                });
            }
        }

        private void BuildStatus()
        {
            StatusBars.Clear();
            var max = Math.Max(1, Enum.GetValues<TaskState>().Max(s => _tasks.CountByState(s)));

            foreach (var s in new[] { TaskState.Planned, TaskState.InProgress, TaskState.Blocked, TaskState.Completed })
            {
                var count = _tasks.CountByState(s);
                StatusBars.Add(new BarItem
                {
                    Label = s == TaskState.InProgress ? "IN PROGRESS" : s.ToString().ToUpperInvariant(),
                    Count = count,
                    Fraction = (double)count / max,
                    BrushKey = s switch
                    {
                        TaskState.Planned => "BrushPlanned",
                        TaskState.InProgress => "BrushAccent",
                        TaskState.Blocked => "BrushCritical",
                        _ => "BrushCompleted"
                    }
                });
            }
        }

        /// <summary>Completions per day for the trailing week.</summary>
        private void BuildHistory()
        {
            HistoryBars.Clear();
            var counts = new int[7];

            for (int i = 0; i < 7; i++)
            {
                var day = DateTime.Today.AddDays(-6 + i);
                counts[i] = _tasks.Tasks.Count(t => t.CompletedUtc.HasValue &&
                                                     t.CompletedUtc.Value.ToLocalTime().Date == day);
            }

            var max = Math.Max(1, counts.Max());
            for (int i = 0; i < 7; i++)
            {
                var day = DateTime.Today.AddDays(-6 + i);
                HistoryBars.Add(new BarItem
                {
                    Label = day.ToString("ddd").ToUpperInvariant().Substring(0, 2),
                    Sub = day.ToString("dd"),
                    Count = counts[i],
                    Fraction = (double)counts[i] / max,
                    BrushKey = "BrushAccent",
                    IsHighlighted = day == DateTime.Today
                });
            }
        }
    }
}

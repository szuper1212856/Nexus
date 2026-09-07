using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using NEXUS.Common;
using NEXUS.Models;
using NEXUS.Services;

namespace NEXUS.ViewModels
{
    public class TasksViewModel : ViewModelBase
    {
        private readonly TaskService _tasks;
        private readonly ListCollectionView _view;

        private string _search = "";
        private string _stateFilter = "ALL";
        private string _priorityFilter = "ALL";
        private string _sortMode = "SMART";
        private bool _hideCompleted;

        public TasksViewModel(TaskService tasks, TaskCommands commands)
        {
            _tasks = tasks;
            Commands = commands;

            // A dedicated view keeps the task page's filtering independent of other sections.
            _view = new ListCollectionView(_tasks.Tasks) { Filter = FilterTask };
            ApplySort();

            _tasks.Changed += Refresh;

            ClearFiltersCommand = new RelayCommand(_ =>
            {
                Search = "";
                StateFilter = "ALL";
                PriorityFilter = "ALL";
                HideCompleted = false;
            });

            SetStateFilterCommand = new RelayCommand(p => StateFilter = p?.ToString() ?? "ALL");
            SetPriorityFilterCommand = new RelayCommand(p => PriorityFilter = p?.ToString() ?? "ALL");
        }

        public TaskCommands Commands { get; }
        public ICollectionView Tasks => _view;

        public ICommand ClearFiltersCommand { get; }
        public ICommand SetStateFilterCommand { get; }
        public ICommand SetPriorityFilterCommand { get; }

        public IReadOnlyList<string> SortModes { get; } = new[] { "SMART", "DEADLINE", "PRIORITY", "NAME", "PROGRESS", "CREATED" };

        public string Search
        {
            get => _search;
            set { if (Set(ref _search, value)) Refresh(); }
        }

        public string StateFilter
        {
            get => _stateFilter;
            set { if (Set(ref _stateFilter, value)) Refresh(); }
        }

        public string PriorityFilter
        {
            get => _priorityFilter;
            set { if (Set(ref _priorityFilter, value)) Refresh(); }
        }

        public bool HideCompleted
        {
            get => _hideCompleted;
            set { if (Set(ref _hideCompleted, value)) Refresh(); }
        }

        public string SortMode
        {
            get => _sortMode;
            set { if (Set(ref _sortMode, value)) { ApplySort(); Refresh(); } }
        }

        public int VisibleCount => _view.Count;
        public int TotalCount => _tasks.Total;
        public bool IsEmpty => _view.Count == 0;

        public string ResultSummary => _view.Count == _tasks.Total
            ? _tasks.Total + " TASKS"
            : _view.Count + " OF " + _tasks.Total + " TASKS";

        /// <summary>Called by quick actions to jump straight into a pre-filtered list.</summary>
        public void ApplyPreset(string preset)
        {
            Search = "";
            switch (preset)
            {
                case "TODAY":
                    StateFilter = "TODAY";
                    PriorityFilter = "ALL";
                    HideCompleted = true;
                    break;
                case "DEADLINE":
                    StateFilter = "DEADLINE";
                    PriorityFilter = "ALL";
                    HideCompleted = true;
                    break;
                case "COMPLETED":
                    StateFilter = "Completed";
                    PriorityFilter = "ALL";
                    HideCompleted = false;
                    break;
                case "CRITICAL":
                    StateFilter = "ALL";
                    PriorityFilter = "Critical";
                    HideCompleted = true;
                    break;
                default:
                    StateFilter = "ALL";
                    PriorityFilter = "ALL";
                    HideCompleted = false;
                    break;
            }
        }

        public override void OnActivated() => Refresh();

        private void Refresh()
        {
            _view.Refresh();
            OnPropertiesChanged(nameof(VisibleCount), nameof(TotalCount), nameof(IsEmpty), nameof(ResultSummary));
        }

        private bool FilterTask(object item)
        {
            if (item is not MissionTask t) return false;

            if (HideCompleted && t.IsCompleted) return false;

            if (!string.IsNullOrWhiteSpace(Search))
            {
                var q = Search.Trim();
                var hit = Contains(t.Title, q) || Contains(t.Notes, q) || Contains(t.StateLabel, q) || Contains(t.PriorityLabel, q);
                if (!hit && t.HasSubTasks)
                    foreach (var s in t.SubTasks)
                        if (Contains(s.Title, q)) { hit = true; break; }
                if (!hit) return false;
            }

            switch (StateFilter)
            {
                case "ALL": break;
                case "ACTIVE": if (t.IsCompleted) return false; break;
                case "OVERDUE": if (!t.IsOverdue) return false; break;
                case "TODAY": if (!t.IsDueToday) return false; break;
                case "DEADLINE":
                    var missionDeadline = AppServices.Settings.MissionDeadline;
                    if (!t.Deadline.HasValue || t.Deadline.Value.Date != missionDeadline.Date) return false;
                    break;
                default:
                    if (Enum.TryParse<TaskState>(StateFilter, out var st) && t.State != st) return false;
                    break;
            }

            if (PriorityFilter != "ALL" && Enum.TryParse<TaskPriority>(PriorityFilter, out var pr) && t.Priority != pr)
                return false;

            return true;
        }

        private static bool Contains(string haystack, string needle)
            => !string.IsNullOrEmpty(haystack) &&
               haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        private void ApplySort()
        {
            _view.CustomSort = new TaskComparer(SortMode);
        }

        /// <summary>Sorting strategies. SMART surfaces overdue and critical work first.</summary>
        private class TaskComparer : System.Collections.IComparer
        {
            private readonly string _mode;
            public TaskComparer(string mode) => _mode = mode;

            public int Compare(object x, object y)
            {
                if (x is not MissionTask a || y is not MissionTask b) return 0;

                switch (_mode)
                {
                    case "DEADLINE":
                        var da = a.Deadline ?? DateTime.MaxValue;
                        var db = b.Deadline ?? DateTime.MaxValue;
                        var byDate = da.CompareTo(db);
                        return byDate != 0 ? byDate : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);

                    case "PRIORITY":
                        var byPriority = a.Priority.CompareTo(b.Priority);
                        return byPriority != 0 ? byPriority : a.SortWeight.CompareTo(b.SortWeight);

                    case "NAME":
                        return string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);

                    case "PROGRESS":
                        return b.Progress.CompareTo(a.Progress);

                    case "CREATED":
                        return b.CreatedUtc.CompareTo(a.CreatedUtc);

                    default:
                        return a.SortWeight.CompareTo(b.SortWeight);
                }
            }
        }
    }
}

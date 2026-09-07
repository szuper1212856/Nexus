using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text.Json.Serialization;
using NEXUS.Common;

namespace NEXUS.Models
{
    /// <summary>
    /// A single unit of work. Stored values are serialised to JSON; everything marked
    /// [JsonIgnore] is derived at runtime for the UI.
    /// </summary>
    public class MissionTask : ObservableObject
    {
        private string _title = "";
        private string _notes = "";
        private TaskPriority _priority = TaskPriority.Normal;
        private TaskState _state = TaskState.Planned;
        private DateTime? _deadline;
        private int _progress;
        private DateTime? _completedUtc;
        private ObservableCollection<SubTask> _subTasks = new ObservableCollection<SubTask>();

        public MissionTask()
        {
            HookSubTasks();
        }

        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public string Title
        {
            get => _title;
            set { if (Set(ref _title, value)) OnPropertyChanged(nameof(Initials)); }
        }

        public string Notes
        {
            get => _notes;
            set { if (Set(ref _notes, value)) OnPropertiesChanged(nameof(HasNotes), nameof(HasDetail)); }
        }

        public TaskPriority Priority
        {
            get => _priority;
            set { if (Set(ref _priority, value)) OnPropertiesChanged(nameof(PriorityLabel), nameof(SortWeight)); }
        }

        public TaskState State
        {
            get => _state;
            set
            {
                if (Set(ref _state, value))
                    OnPropertiesChanged(nameof(StateLabel), nameof(IsCompleted), nameof(IsActive),
                        nameof(IsOverdue), nameof(DeadlineDisplay), nameof(SortWeight));
            }
        }

        public DateTime? Deadline
        {
            get => _deadline;
            set { if (Set(ref _deadline, value)) OnPropertiesChanged(nameof(DeadlineDisplay), nameof(IsOverdue), nameof(IsDueToday), nameof(SortWeight)); }
        }

        /// <summary>0-100. Auto-derived from subtasks when any exist.</summary>
        public int Progress
        {
            get => _progress;
            set
            {
                var clamped = value < 0 ? 0 : (value > 100 ? 100 : value);
                if (Set(ref _progress, clamped)) OnPropertyChanged(nameof(ProgressFraction));
            }
        }

        public DateTime? CompletedUtc
        {
            get => _completedUtc;
            set => Set(ref _completedUtc, value);
        }

        public ObservableCollection<SubTask> SubTasks
        {
            get => _subTasks;
            set
            {
                UnhookSubTasks();
                Set(ref _subTasks, value ?? new ObservableCollection<SubTask>());
                HookSubTasks();
                RefreshSubTaskState();
            }
        }

        // ---------- derived / display ----------

        [JsonIgnore] public bool IsCompleted => State == TaskState.Completed;
        [JsonIgnore] public bool IsActive => State != TaskState.Completed;
        [JsonIgnore] public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
        [JsonIgnore] public bool HasSubTasks => SubTasks != null && SubTasks.Count > 0;
        [JsonIgnore] public bool HasDetail => HasNotes || HasSubTasks;
        [JsonIgnore] public double ProgressFraction => Progress / 100.0;

        [JsonIgnore]
        public string Initials
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Title)) return "--";
                var parts = Title.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) return parts[0].Substring(0, Math.Min(2, parts[0].Length)).ToUpperInvariant();
                return (parts[0].Substring(0, 1) + parts[1].Substring(0, 1)).ToUpperInvariant();
            }
        }

        [JsonIgnore]
        public string PriorityLabel => Priority switch
        {
            TaskPriority.Critical => "CRITICAL",
            TaskPriority.High => "HIGH",
            TaskPriority.Normal => "NORMAL",
            _ => "LOW"
        };

        [JsonIgnore]
        public string StateLabel => State switch
        {
            TaskState.Planned => "PLANNED",
            TaskState.InProgress => "IN PROGRESS",
            TaskState.Blocked => "BLOCKED",
            _ => "COMPLETED"
        };

        [JsonIgnore]
        public string SubTaskSummary => HasSubTasks
            ? SubTasks.Count(s => s.IsDone) + "/" + SubTasks.Count
            : "";

        [JsonIgnore] public bool IsOverdue => Deadline.HasValue && !IsCompleted && Deadline.Value < DateTime.Now;

        [JsonIgnore] public bool IsDueToday => Deadline.HasValue && Deadline.Value.Date == DateTime.Today;

        [JsonIgnore]
        public string DeadlineDisplay
        {
            get
            {
                if (!Deadline.HasValue) return "NO DEADLINE";
                var d = Deadline.Value;
                var days = (d.Date - DateTime.Today).Days;
                var time = d.ToString("HH:mm");
                if (IsOverdue) return "OVERDUE \u00B7 " + d.ToString("ddd dd MMM").ToUpperInvariant();
                if (days == 0) return "TODAY " + time;
                if (days == 1) return "TOMORROW " + time;
                if (days > 1 && days < 7) return d.ToString("ddd").ToUpperInvariant() + " " + time;
                return d.ToString("dd MMM").ToUpperInvariant() + " " + time;
            }
        }

        /// <summary>Used by the default "smart" sort: overdue first, then priority, then deadline.</summary>
        [JsonIgnore]
        public double SortWeight
        {
            get
            {
                double w = (int)Priority * 100;
                if (IsOverdue) w -= 1000;
                if (Deadline.HasValue) w += Math.Min(500, (Deadline.Value - DateTime.Now).TotalHours);
                else w += 500;
                if (IsCompleted) w += 100000;
                return w;
            }
        }

        // ---------- subtask plumbing ----------

        private void HookSubTasks()
        {
            if (_subTasks == null) return;
            _subTasks.CollectionChanged += OnSubTasksChanged;
            foreach (var s in _subTasks) s.PropertyChanged += OnSubTaskPropertyChanged;
        }

        private void UnhookSubTasks()
        {
            if (_subTasks == null) return;
            _subTasks.CollectionChanged -= OnSubTasksChanged;
            foreach (var s in _subTasks) s.PropertyChanged -= OnSubTaskPropertyChanged;
        }

        private void OnSubTasksChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (SubTask s in e.OldItems) s.PropertyChanged -= OnSubTaskPropertyChanged;
            if (e.NewItems != null)
                foreach (SubTask s in e.NewItems) s.PropertyChanged += OnSubTaskPropertyChanged;
            RefreshSubTaskState();
        }

        private void OnSubTaskPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SubTask.IsDone)) RefreshSubTaskState();
        }

        /// <summary>When subtasks exist they drive the task's progress percentage.</summary>
        public void RefreshSubTaskState()
        {
            OnPropertiesChanged(nameof(HasSubTasks), nameof(SubTaskSummary), nameof(HasDetail));
            if (!HasSubTasks || IsCompleted) return;
            Progress = (int)Math.Round(SubTasks.Count(s => s.IsDone) * 100.0 / SubTasks.Count);
        }

        /// <summary>Re-evaluates time-dependent display values. Called from the shell clock tick.</summary>
        public void RefreshTimeDependent()
            => OnPropertiesChanged(nameof(IsOverdue), nameof(IsDueToday), nameof(DeadlineDisplay));

        public MissionTask Clone()
        {
            var copy = new MissionTask
            {
                Id = Id,
                CreatedUtc = CreatedUtc,
                Title = Title,
                Notes = Notes,
                Priority = Priority,
                State = State,
                Deadline = Deadline,
                Progress = Progress,
                CompletedUtc = CompletedUtc
            };
            foreach (var s in SubTasks)
                copy.SubTasks.Add(new SubTask { Id = s.Id, Title = s.Title, IsDone = s.IsDone });
            return copy;
        }

        /// <summary>Copies edited values back onto the live instance so bindings stay attached.</summary>
        public void CopyFrom(MissionTask other)
        {
            Title = other.Title;
            Notes = other.Notes;
            Priority = other.Priority;
            State = other.State;
            Deadline = other.Deadline;
            Progress = other.Progress;
            CompletedUtc = other.CompletedUtc;

            SubTasks.Clear();
            foreach (var s in other.SubTasks)
                SubTasks.Add(new SubTask { Id = s.Id, Title = s.Title, IsDone = s.IsDone });
            RefreshSubTaskState();
        }
    }
}

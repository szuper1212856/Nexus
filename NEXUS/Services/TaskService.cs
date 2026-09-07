using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using NEXUS.Models;

namespace NEXUS.Services
{
    /// <summary>
    /// The single source of truth for tasks. Every mutation goes through here so that
    /// the activity log, persistence and statistics stay consistent.
    /// </summary>
    public class TaskService
    {
        private readonly DataStore _store;
        private readonly ActivityService _activity;

        public ObservableCollection<MissionTask> Tasks => _store.Data.Tasks;

        /// <summary>Raised whenever the set of tasks or any task property changes.</summary>
        public event Action Changed;

        /// <summary>Raised when a task transitions into Completed, so the UI can play its animation.</summary>
        public event Action<MissionTask> TaskCompleted;

        public TaskService(DataStore store, ActivityService activity)
        {
            _store = store;
            _activity = activity;

            Tasks.CollectionChanged += OnCollectionChanged;
            foreach (var t in Tasks) t.PropertyChanged += OnTaskPropertyChanged;
        }

        // ---------- statistics ----------

        public int Total => Tasks.Count;
        public int Completed => Tasks.Count(t => t.IsCompleted);
        public int Remaining => Tasks.Count(t => !t.IsCompleted);
        public int Blocked => Tasks.Count(t => t.State == TaskState.Blocked);
        public int InProgress => Tasks.Count(t => t.State == TaskState.InProgress);
        public int Overdue => Tasks.Count(t => t.IsOverdue);
        public double CompletionFraction => Total == 0 ? 0 : (double)Completed / Total;
        public int CompletionPercent => (int)Math.Round(CompletionFraction * 100);

        public int CountByPriority(TaskPriority p) => Tasks.Count(t => t.Priority == p);
        public int CountByState(TaskState s) => Tasks.Count(t => t.State == s);

        // ---------- mutations ----------

        public void Add(MissionTask task)
        {
            if (task == null) return;
            Tasks.Add(task);
            _activity.Log(ActivityKind.Created, "New task created", task.Title);
            Touch();
        }

        public void Delete(MissionTask task)
        {
            if (task == null || !Tasks.Contains(task)) return;
            Tasks.Remove(task);
            _activity.Log(ActivityKind.Deleted, "Task deleted", task.Title);
            Touch();
        }

        public void Complete(MissionTask task)
        {
            if (task == null || task.IsCompleted) return;
            task.State = TaskState.Completed;
            task.CompletedUtc = DateTime.UtcNow;
            task.Progress = 100;
            foreach (var s in task.SubTasks) s.IsDone = true;

            _activity.Log(ActivityKind.Completed, "Task completed", task.Title);
            TaskCompleted?.Invoke(task);
            Touch();
        }

        public void Reopen(MissionTask task)
        {
            if (task == null || !task.IsCompleted) return;
            task.State = TaskState.InProgress;
            task.CompletedUtc = null;
            if (task.HasSubTasks) task.RefreshSubTaskState();
            else if (task.Progress >= 100) task.Progress = 50;

            _activity.Log(ActivityKind.Reopened, "Task reopened", task.Title);
            Touch();
        }

        public void ToggleComplete(MissionTask task)
        {
            if (task == null) return;
            if (task.IsCompleted) Reopen(task);
            else Complete(task);
        }

        public void SetPriority(MissionTask task, TaskPriority priority)
        {
            if (task == null || task.Priority == priority) return;
            var old = task.PriorityLabel;
            task.Priority = priority;
            _activity.Log(ActivityKind.Priority, "Task priority changed",
                task.Title + " \u00B7 " + old + " \u2192 " + task.PriorityLabel);
            Touch();
        }

        public void SetState(MissionTask task, TaskState state)
        {
            if (task == null || task.State == state) return;

            if (state == TaskState.Completed) { Complete(task); return; }
            if (task.IsCompleted) { Reopen(task); }

            var old = task.StateLabel;
            task.State = state;
            task.CompletedUtc = null;
            _activity.Log(ActivityKind.Status, "Task status changed",
                task.Title + " \u00B7 " + old + " \u2192 " + task.StateLabel);
            Touch();
        }

        /// <summary>Called after the editor dialog writes changes back onto a task.</summary>
        public void NotifyEdited(MissionTask task)
        {
            _activity.Log(ActivityKind.Edited, "Task updated", task?.Title ?? "");
            Touch();
        }

        /// <summary>Replaces the whole collection, used by import and reset.</summary>
        public void ReplaceAll(ObservableCollection<MissionTask> tasks)
        {
            Tasks.CollectionChanged -= OnCollectionChanged;
            foreach (var t in Tasks) t.PropertyChanged -= OnTaskPropertyChanged;

            Tasks.Clear();
            foreach (var t in tasks) Tasks.Add(t);

            Tasks.CollectionChanged += OnCollectionChanged;
            foreach (var t in Tasks) t.PropertyChanged += OnTaskPropertyChanged;
            Touch();
        }

        /// <summary>Refreshes overdue / countdown text on every task. Driven by the shell clock.</summary>
        public void RefreshTimeDependent()
        {
            foreach (var t in Tasks) t.RefreshTimeDependent();
        }

        public void Touch()
        {
            Changed?.Invoke();
            _store.RequestSave();
        }

        private void OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (MissionTask t in e.OldItems) t.PropertyChanged -= OnTaskPropertyChanged;
            if (e.NewItems != null)
                foreach (MissionTask t in e.NewItems) t.PropertyChanged += OnTaskPropertyChanged;
            Changed?.Invoke();
        }

        private void OnTaskPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            Changed?.Invoke();
            _store.RequestSave();
        }

        /// <summary>Seed set written on first launch so the console is never empty.</summary>
        public void SeedInitialTasks()
        {
            var deadline = _store.Data.Settings.MissionDeadline;
            var soon = DateTime.Today.AddHours(18);

            void Add(string title, TaskPriority p, TaskState s, int progress, DateTime? due, string notes, params string[] subs)
            {
                var t = new MissionTask
                {
                    Title = title,
                    Priority = p,
                    State = s,
                    Progress = progress,
                    Deadline = due,
                    Notes = notes
                };
                foreach (var sub in subs) t.SubTasks.Add(new SubTask { Title = sub });
                if (s == TaskState.Completed) { t.CompletedUtc = DateTime.UtcNow; t.Progress = 100; foreach (var x in t.SubTasks) x.IsDone = true; }
                Tasks.Add(t);
            }

            Add("Define mission scope", TaskPriority.Critical, TaskState.Completed, 100, soon, "Baseline agreed.");
            Add("Draft execution plan", TaskPriority.High, TaskState.InProgress, 60, soon.AddDays(1),
                "Break the remaining work into deliverable units.", "Outline phases", "Assign effort", "Review");
            Add("Prepare working environment", TaskPriority.Normal, TaskState.Completed, 100, soon, "");
            Add("Collect required inputs", TaskPriority.High, TaskState.InProgress, 35, soon.AddDays(1), "",
                "Gather sources", "Verify formats");
            Add("Primary deliverable \u2014 first pass", TaskPriority.Critical, TaskState.InProgress, 20, deadline,
                "The single most important item before the deadline.", "Structure", "Content", "Polish");
            Add("Secondary deliverable", TaskPriority.Normal, TaskState.Planned, 0, deadline, "");
            Add("Resolve blocking dependency", TaskPriority.Critical, TaskState.Blocked, 10, soon.AddDays(1),
                "Waiting on an external response.");
            Add("Quality review pass", TaskPriority.High, TaskState.Planned, 0, deadline, "");
            Add("Prepare handover notes", TaskPriority.Low, TaskState.Planned, 0, deadline, "");
            Add("Final verification and submit", TaskPriority.Critical, TaskState.Planned, 0, deadline,
                "Nothing ships until this passes.", "Check deliverables", "Confirm deadline", "Submit");

            _activity.Log(ActivityKind.System, "Command center initialised", Tasks.Count + " tasks provisioned");
            Touch();
        }
    }
}

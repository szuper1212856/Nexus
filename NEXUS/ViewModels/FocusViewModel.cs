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
    public class FocusViewModel : ViewModelBase
    {
        private readonly FocusService _focus;
        private readonly TaskService _tasks;
        private readonly ToastService _toasts;
        private readonly DataStore _store;

        private MissionTask _selectedTask;
        private int _duration;

        public FocusViewModel(FocusService focus, TaskService tasks, ToastService toasts, DataStore store)
        {
            _focus = focus;
            _tasks = tasks;
            _toasts = toasts;
            _store = store;
            _duration = store.Data.Settings.DefaultFocusMinutes;

            _tasks.Changed += RefreshCandidates;
            _focus.PropertyChanged += (s, e) => OnPropertyChanged(nameof(Timer));

            StartCommand = new RelayCommand(_ => Start(), _ => !_focus.IsRunning);
            PauseCommand = new RelayCommand(_ => _focus.TogglePause(), _ => _focus.IsRunning);
            CompleteCommand = new RelayCommand(_ => CompleteTask(), _ => _focus.IsRunning);
            ExitCommand = new RelayCommand(_ => Exit(), _ => _focus.IsRunning);
            SetDurationCommand = new RelayCommand(p =>
            {
                if (int.TryParse(p?.ToString(), out var m)) Duration = m;
            });

            RefreshCandidates();
        }

        public FocusService Timer => _focus;

        public ObservableCollection<MissionTask> Candidates { get; } = new ObservableCollection<MissionTask>();
        public ObservableCollection<FocusSession> History { get; } = new ObservableCollection<FocusSession>();

        public IReadOnlyList<int> Durations { get; } = new[] { 15, 25, 45, 60 };

        public ICommand StartCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand CompleteCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand SetDurationCommand { get; }

        public MissionTask SelectedTask
        {
            get => _selectedTask;
            set => Set(ref _selectedTask, value);
        }

        public int Duration
        {
            get => _duration;
            set { if (Set(ref _duration, value)) OnPropertyChanged(nameof(DurationKey)); }
        }

        public string DurationKey => Duration.ToString();

        public int SessionsToday => _store.Data.FocusSessions
            .Count(s => s.StartedUtc.ToLocalTime().Date == DateTime.Today);

        public string FocusedTimeToday
        {
            get
            {
                var seconds = _store.Data.FocusSessions
                    .Where(s => s.StartedUtc.ToLocalTime().Date == DateTime.Today)
                    .Sum(s => s.ElapsedSeconds);
                var span = TimeSpan.FromSeconds(seconds);
                return (int)span.TotalHours + "h " + span.Minutes + "m";
            }
        }

        public override void OnActivated()
        {
            RefreshCandidates();
            RefreshHistory();
        }

        /// <summary>Entry point used by quick actions and per-task focus buttons.</summary>
        public void StartFor(MissionTask task)
        {
            if (_focus.IsRunning) return;
            if (task != null) SelectedTask = task;
            Start();
        }

        private void Start()
        {
            var task = SelectedTask ?? Candidates.FirstOrDefault();
            SelectedTask = task;
            _focus.Start(task, Duration);
            _toasts.Show("FOCUS SESSION STARTED", Duration + " minutes \u00B7 " + (task?.Title ?? "unassigned"), ToastKind.Info);
            RefreshStats();
        }

        private void CompleteTask()
        {
            var task = _focus.Task;
            _focus.Stop(false);
            if (task != null && !task.IsCompleted)
            {
                _tasks.Complete(task);
                _toasts.Show("TASK COMPLETED", task.Title, ToastKind.Success);
            }
            RefreshHistory();
            RefreshStats();
        }

        private void Exit()
        {
            _focus.Stop(false);
            RefreshHistory();
            RefreshStats();
        }

        public void OnSessionFinished()
        {
            RefreshHistory();
            RefreshStats();
        }

        private void RefreshCandidates()
        {
            var previous = SelectedTask;
            Candidates.Clear();
            foreach (var t in _tasks.Tasks.Where(t => !t.IsCompleted).OrderBy(t => t.SortWeight))
                Candidates.Add(t);

            if (previous != null && Candidates.Contains(previous)) SelectedTask = previous;
            else if (SelectedTask == null || !Candidates.Contains(SelectedTask))
                SelectedTask = Candidates.FirstOrDefault();
        }

        private void RefreshHistory()
        {
            History.Clear();
            foreach (var s in _store.Data.FocusSessions.OrderByDescending(s => s.StartedUtc).Take(8))
                History.Add(s);
        }

        private void RefreshStats()
            => OnPropertiesChanged(nameof(SessionsToday), nameof(FocusedTimeToday));
    }
}

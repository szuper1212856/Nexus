using System;
using System.Windows.Threading;
using NEXUS.Common;
using NEXUS.Models;

namespace NEXUS.Services
{
    /// <summary>Drives the focus (pomodoro-style) session. One session at a time.</summary>
    public class FocusService : ObservableObject
    {
        private readonly DataStore _store;
        private readonly ActivityService _activity;
        private readonly DispatcherTimer _timer;

        private MissionTask _task;
        private bool _isRunning;
        private bool _isPaused;
        private int _plannedSeconds;
        private int _remainingSeconds;
        private FocusSession _session;

        public FocusService(DataStore store, ActivityService activity)
        {
            _store = store;
            _activity = activity;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += OnTick;
        }

        /// <summary>Raised when the countdown reaches zero.</summary>
        public event Action<MissionTask> SessionFinished;

        public MissionTask Task
        {
            get => _task;
            private set { Set(ref _task, value); OnPropertyChanged(nameof(TaskTitle)); }
        }

        public string TaskTitle => Task?.Title ?? "UNASSIGNED SESSION";

        public bool IsRunning
        {
            get => _isRunning;
            private set { Set(ref _isRunning, value); OnPropertyChanged(nameof(IsIdle)); }
        }

        public bool IsIdle => !IsRunning;

        public bool IsPaused
        {
            get => _isPaused;
            private set { Set(ref _isPaused, value); OnPropertyChanged(nameof(StateLabel)); }
        }

        public int PlannedMinutes => _plannedSeconds / 60;

        public int RemainingSeconds
        {
            get => _remainingSeconds;
            private set
            {
                if (Set(ref _remainingSeconds, value))
                    OnPropertiesChanged(nameof(TimeDisplay), nameof(Fraction), nameof(ElapsedDisplay));
            }
        }

        public string TimeDisplay => TimeSpan.FromSeconds(Math.Max(0, RemainingSeconds)).ToString(@"mm\:ss");

        public string ElapsedDisplay
            => TimeSpan.FromSeconds(Math.Max(0, _plannedSeconds - RemainingSeconds)).ToString(@"mm\:ss");

        /// <summary>0-1 progress through the session, used by the ring gauge.</summary>
        public double Fraction
            => _plannedSeconds <= 0 ? 0 : 1.0 - (double)RemainingSeconds / _plannedSeconds;

        public string StateLabel => !IsRunning ? "STANDBY" : (IsPaused ? "PAUSED" : "ACTIVE");

        public void Start(MissionTask task, int minutes)
        {
            if (minutes <= 0) minutes = 25;
            Task = task;
            _plannedSeconds = minutes * 60;
            RemainingSeconds = _plannedSeconds;

            _session = new FocusSession
            {
                TaskId = task?.Id,
                TaskTitle = task?.Title ?? "Unassigned",
                PlannedMinutes = minutes
            };

            IsRunning = true;
            IsPaused = false;
            _timer.Start();
            OnPropertyChanged(nameof(PlannedMinutes));
            OnPropertyChanged(nameof(StateLabel));

            _activity.Log(ActivityKind.Focus, "Focus timer started",
                minutes + " min \u00B7 " + (task?.Title ?? "unassigned"));
        }

        public void Pause()
        {
            if (!IsRunning || IsPaused) return;
            _timer.Stop();
            IsPaused = true;
            _activity.Log(ActivityKind.Focus, "Focus session paused", Task?.Title ?? "");
        }

        public void Resume()
        {
            if (!IsRunning || !IsPaused) return;
            _timer.Start();
            IsPaused = false;
            _activity.Log(ActivityKind.Focus, "Focus session resumed", Task?.Title ?? "");
        }

        public void TogglePause()
        {
            if (IsPaused) Resume(); else Pause();
        }

        /// <summary>Ends the session and records it. <paramref name="finished"/> marks a full run.</summary>
        public void Stop(bool finished, bool log = true)
        {
            if (!IsRunning) return;
            _timer.Stop();

            if (_session != null)
            {
                _session.ElapsedSeconds = _plannedSeconds - Math.Max(0, RemainingSeconds);
                _session.Finished = finished;
                _store.Data.FocusSessions.Add(_session);
                _store.RequestSave();
                _session = null;
            }

            IsRunning = false;
            IsPaused = false;
            RemainingSeconds = 0;
            OnPropertyChanged(nameof(StateLabel));

            if (log)
                _activity.Log(ActivityKind.Focus,
                    finished ? "Focus session finished" : "Focus session ended",
                    Task?.Title ?? "");
        }

        private void OnTick(object sender, EventArgs e)
        {
            RemainingSeconds = RemainingSeconds - 1;
            if (RemainingSeconds > 0) return;

            var task = Task;
            Stop(true, log: false);
            _activity.Log(ActivityKind.Focus, "Focus session finished", task?.Title ?? "");
            SessionFinished?.Invoke(task);
        }
    }
}

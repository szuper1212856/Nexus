using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using NEXUS.Common;
using NEXUS.Models;
using NEXUS.Services;

namespace NEXUS.ViewModels
{
    /// <summary>
    /// Backs the add/edit dialog. Edits a detached clone so cancelling leaves the
    /// original task untouched.
    /// </summary>
    public class TaskEditorViewModel : ViewModelBase
    {
        private readonly MissionTask _draft;
        private string _dateText = "";
        private string _timeText = "18:00";
        private string _newSubTask = "";
        private string _error = "";

        public event Action<bool> CloseRequested;

        public TaskEditorViewModel(MissionTask source, string heading)
        {
            Heading = heading;
            _draft = source?.Clone() ?? new MissionTask { Priority = TaskPriority.Normal, State = TaskState.Planned };

            if (_draft.Deadline.HasValue)
            {
                _dateText = _draft.Deadline.Value.ToString("yyyy-MM-dd");
                _timeText = _draft.Deadline.Value.ToString("HH:mm");
            }

            SaveCommand = new RelayCommand(_ => Save(), _ => !string.IsNullOrWhiteSpace(Title));
            CancelCommand = new RelayCommand(_ => CloseRequested?.Invoke(false));
            AddSubTaskCommand = new RelayCommand(_ => AddSubTask(), _ => !string.IsNullOrWhiteSpace(NewSubTask));
            RemoveSubTaskCommand = new RelayCommand(p => { if (p is SubTask s) SubTasks.Remove(s); });
            SetPriorityCommand = new RelayCommand(p => { if (Enum.TryParse<TaskPriority>(p?.ToString(), out var v)) Priority = v; });
            SetStateCommand = new RelayCommand(p => { if (Enum.TryParse<TaskState>(p?.ToString(), out var v)) State = v; });
            QuickDateCommand = new RelayCommand(p => ApplyQuickDate(p?.ToString()));
            ClearDeadlineCommand = new RelayCommand(_ => { DateText = ""; TimeText = "18:00"; });
        }

        public string Heading { get; }
        public MissionTask Result { get; private set; }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand AddSubTaskCommand { get; }
        public ICommand RemoveSubTaskCommand { get; }
        public ICommand SetPriorityCommand { get; }
        public ICommand SetStateCommand { get; }
        public ICommand QuickDateCommand { get; }
        public ICommand ClearDeadlineCommand { get; }

        public string Title
        {
            get => _draft.Title;
            set { _draft.Title = value; OnPropertyChanged(); }
        }

        public string Notes
        {
            get => _draft.Notes;
            set { _draft.Notes = value; OnPropertyChanged(); }
        }

        public TaskPriority Priority
        {
            get => _draft.Priority;
            set { _draft.Priority = value; OnPropertyChanged(); OnPropertyChanged(nameof(PriorityKey)); }
        }

        public string PriorityKey => Priority.ToString();

        public TaskState State
        {
            get => _draft.State;
            set { _draft.State = value; OnPropertyChanged(); OnPropertyChanged(nameof(StateKey)); }
        }

        public string StateKey => State.ToString();

        public int Progress
        {
            get => _draft.Progress;
            set { _draft.Progress = value; OnPropertyChanged(); }
        }

        public ObservableCollection<SubTask> SubTasks => _draft.SubTasks;

        public string DateText
        {
            get => _dateText;
            set { Set(ref _dateText, value); Error = ""; OnPropertyChanged(nameof(DeadlinePreview)); }
        }

        public string TimeText
        {
            get => _timeText;
            set { Set(ref _timeText, value); Error = ""; OnPropertyChanged(nameof(DeadlinePreview)); }
        }

        public string NewSubTask
        {
            get => _newSubTask;
            set => Set(ref _newSubTask, value);
        }

        public string Error
        {
            get => _error;
            private set => Set(ref _error, value);
        }

        public string DeadlinePreview
        {
            get
            {
                if (string.IsNullOrWhiteSpace(DateText)) return "NO DEADLINE SET";
                return TryBuildDeadline(out var dt, out var err)
                    ? (dt.HasValue ? dt.Value.ToString("dddd, dd MMMM yyyy \u00B7 HH:mm").ToUpperInvariant() : "NO DEADLINE SET")
                    : err;
            }
        }

        private void ApplyQuickDate(string key)
        {
            var now = DateTime.Now;
            DateTime target = key switch
            {
                "TODAY" => now.Date,
                "TOMORROW" => now.Date.AddDays(1),
                "WEDNESDAY" => AppSettings.NextWednesday(now).Date,
                "MISSION" => AppServices.Settings.MissionDeadline.Date,
                _ => now.Date
            };

            DateText = target.ToString("yyyy-MM-dd");
            if (key == "MISSION") TimeText = AppServices.Settings.MissionDeadline.ToString("HH:mm");
        }

        private bool TryBuildDeadline(out DateTime? result, out string error)
        {
            result = null;
            error = "";

            if (string.IsNullOrWhiteSpace(DateText)) return true;

            if (!DateTime.TryParseExact(DateText.Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                error = "DATE MUST BE YYYY-MM-DD";
                return false;
            }

            var time = TimeSpan.FromHours(18);
            if (!string.IsNullOrWhiteSpace(TimeText) &&
                !TimeSpan.TryParseExact(TimeText.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out time))
            {
                error = "TIME MUST BE HH:MM";
                return false;
            }

            result = date.Add(time);
            return true;
        }

        private void AddSubTask()
        {
            SubTasks.Add(new SubTask { Title = NewSubTask.Trim() });
            NewSubTask = "";
        }

        private void Save()
        {
            if (string.IsNullOrWhiteSpace(Title))
            {
                Error = "TASK NAME IS REQUIRED";
                return;
            }

            if (!TryBuildDeadline(out var deadline, out var err))
            {
                Error = err;
                return;
            }

            _draft.Title = Title.Trim();
            _draft.Deadline = deadline;

            if (_draft.State == TaskState.Completed)
            {
                _draft.Progress = 100;
                _draft.CompletedUtc ??= DateTime.UtcNow;
            }
            else
            {
                _draft.CompletedUtc = null;
                if (_draft.Progress >= 100) _draft.Progress = 99;
            }

            _draft.RefreshSubTaskState();
            Result = _draft;
            CloseRequested?.Invoke(true);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Input;
using Microsoft.Win32;
using NEXUS.Common;
using NEXUS.Models;
using NEXUS.Services;

// System.Diagnostics also defines ActivityKind, so Process is referenced explicitly instead.
using Process = System.Diagnostics.Process;

namespace NEXUS.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly DataStore _store;
        private readonly ThemeService _theme;
        private readonly TaskService _tasks;
        private readonly ToastService _toasts;
        private readonly ActivityService _activity;

        private string _deadlineDate;
        private string _deadlineTime;
        private string _status = "";

        public SettingsViewModel(DataStore store, ThemeService theme, TaskService tasks,
                                 ToastService toasts, ActivityService activity)
        {
            _store = store;
            _theme = theme;
            _tasks = tasks;
            _toasts = toasts;
            _activity = activity;

            _deadlineDate = Settings.MissionDeadline.ToString("yyyy-MM-dd");
            _deadlineTime = Settings.MissionDeadline.ToString("HH:mm");

            SetThemeCommand = new RelayCommand(p => Theme = p?.ToString());
            SetAccentCommand = new RelayCommand(p => Accent = p?.ToString());
            SetFocusDurationCommand = new RelayCommand(p =>
            {
                if (int.TryParse(p?.ToString(), out var m)) DefaultFocusMinutes = m;
            });

            ApplyDeadlineCommand = new RelayCommand(_ => ApplyDeadline());
            NextWednesdayCommand = new RelayCommand(_ =>
            {
                var next = AppSettings.NextWednesday(DateTime.Now);
                DeadlineDate = next.ToString("yyyy-MM-dd");
                DeadlineTime = next.ToString("HH:mm");
                ApplyDeadline();
            });

            ExportCommand = new RelayCommand(_ => Export());
            ImportCommand = new RelayCommand(_ => Import());
            ResetCommand = new RelayCommand(_ => Reset());
            OpenFolderCommand = new RelayCommand(_ => OpenFolder());
        }

        public AppSettings Settings => _store.Data.Settings;
        public IReadOnlyList<ThemeOption> Themes => _theme.Themes;
        public IReadOnlyList<AccentOption> Accents => _theme.Accents;
        public IReadOnlyList<int> FocusDurations { get; } = new[] { 15, 25, 45, 60 };

        public ICommand SetThemeCommand { get; }
        public ICommand SetAccentCommand { get; }
        public ICommand SetFocusDurationCommand { get; }
        public ICommand ApplyDeadlineCommand { get; }
        public ICommand NextWednesdayCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand OpenFolderCommand { get; }

        public string StoragePath => _store.RootFolder;
        public string Version => "1.0.0";

        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        public string Theme
        {
            get => Settings.Theme;
            set
            {
                if (Settings.Theme == value) return;
                Settings.Theme = value;
                _theme.Apply(Settings.Theme, Settings.Accent);
                Persist();
                OnPropertyChanged();
            }
        }

        public string Accent
        {
            get => Settings.Accent;
            set
            {
                if (Settings.Accent == value) return;
                Settings.Accent = value;
                _theme.Apply(Settings.Theme, Settings.Accent);
                Persist();
                OnPropertyChanged();
            }
        }

        public bool NotificationsEnabled
        {
            get => Settings.NotificationsEnabled;
            set { Settings.NotificationsEnabled = value; Persist(); OnPropertyChanged(); }
        }

        public bool SoundEnabled
        {
            get => Settings.SoundEnabled;
            set { Settings.SoundEnabled = value; Persist(); OnPropertyChanged(); }
        }

        public bool ToastsEnabled
        {
            get => Settings.ToastsEnabled;
            set { Settings.ToastsEnabled = value; Persist(); OnPropertyChanged(); }
        }

        public int DefaultFocusMinutes
        {
            get => Settings.DefaultFocusMinutes;
            set
            {
                Settings.DefaultFocusMinutes = value;
                Persist();
                OnPropertyChanged();
                OnPropertyChanged(nameof(DefaultFocusKey));
            }
        }

        public string DefaultFocusKey => Settings.DefaultFocusMinutes.ToString();

        public bool RestoreLastSection
        {
            get => Settings.RestoreLastSection;
            set { Settings.RestoreLastSection = value; Persist(); OnPropertyChanged(); }
        }

        public bool AutoRollDeadline
        {
            get => Settings.AutoRollDeadline;
            set { Settings.AutoRollDeadline = value; Persist(); OnPropertyChanged(); }
        }

        /// <summary>Writes the Run registry key. Reflects the real OS state, not just the setting.</summary>
        public bool LaunchOnStartup
        {
            get => Settings.LaunchOnStartup;
            set
            {
                var ok = SystemService.SetStartupEnabled(value);
                Settings.LaunchOnStartup = ok && value;
                Persist();
                OnPropertyChanged();
                Status = ok
                    ? (value ? "NEXUS will start with Windows." : "Start-up entry removed.")
                    : "Could not update the start-up entry.";
            }
        }

        public string DeadlineDate
        {
            get => _deadlineDate;
            set => Set(ref _deadlineDate, value);
        }

        public string DeadlineTime
        {
            get => _deadlineTime;
            set => Set(ref _deadlineTime, value);
        }

        public override void OnActivated()
        {
            Settings.LaunchOnStartup = SystemService.IsStartupEnabled();
            _deadlineDate = Settings.MissionDeadline.ToString("yyyy-MM-dd");
            _deadlineTime = Settings.MissionDeadline.ToString("HH:mm");
            OnPropertiesChanged(nameof(LaunchOnStartup), nameof(DeadlineDate), nameof(DeadlineTime),
                nameof(Theme), nameof(Accent), nameof(DefaultFocusKey));
        }

        private void ApplyDeadline()
        {
            if (!DateTime.TryParseExact(DeadlineDate?.Trim(), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                Status = "Date must be in YYYY-MM-DD format.";
                return;
            }

            var time = new TimeSpan(23, 59, 0);
            if (!string.IsNullOrWhiteSpace(DeadlineTime) &&
                !TimeSpan.TryParseExact(DeadlineTime.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out time))
            {
                Status = "Time must be in HH:MM format.";
                return;
            }

            Settings.MissionDeadline = date.Add(time);
            Persist();
            _activity.Log(ActivityKind.System, "Mission deadline updated",
                Settings.MissionDeadline.ToString("ddd dd MMM HH:mm"));
            _toasts.Show("DEADLINE UPDATED", Settings.MissionDeadline.ToString("dddd dd MMM \u00B7 HH:mm"), ToastKind.Info);
            Status = "Deadline updated.";
        }

        private void Export()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export NEXUS data",
                Filter = "NEXUS backup (*.json)|*.json|All files (*.*)|*.*",
                FileName = "nexus-backup-" + DateTime.Now.ToString("yyyy-MM-dd-HHmm") + ".json"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                _store.SaveNow();
                _store.Export(dialog.FileName);
                _activity.Log(ActivityKind.Data, "Data exported", dialog.FileName);
                _toasts.Show("EXPORT COMPLETE", "Backup written to disk.", ToastKind.Success);
                Status = "Exported to " + dialog.FileName;
            }
            catch (Exception ex)
            {
                _toasts.Show("EXPORT FAILED", ex.Message, ToastKind.Critical);
                Status = "Export failed: " + ex.Message;
            }
        }

        private void Import()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Import NEXUS data",
                Filter = "NEXUS backup (*.json)|*.json|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            if (!DialogService.Confirm("IMPORT DATA",
                    "Importing replaces every task, activity entry and setting currently stored. Continue?", "IMPORT"))
                return;

            try
            {
                var bundle = _store.ReadBundle(dialog.FileName);

                _store.Data.Activity = bundle.Activity ?? new List<ActivityEntry>();
                _store.Data.FocusSessions = bundle.FocusSessions ?? new List<FocusSession>();
                if (bundle.Settings != null) _store.Data.Settings = bundle.Settings;

                _tasks.ReplaceAll(bundle.Tasks);
                AppServices.RebindAfterDataChange();

                _activity.Log(ActivityKind.Data, "Data imported", dialog.FileName);
                _store.SaveNow();

                OnActivated();
                _toasts.Show("IMPORT COMPLETE", _tasks.Total + " tasks restored.", ToastKind.Success);
                Status = "Imported from " + dialog.FileName;
            }
            catch (Exception ex)
            {
                _toasts.Show("IMPORT FAILED", ex.Message, ToastKind.Critical);
                Status = "Import failed: " + ex.Message;
            }
        }

        private void Reset()
        {
            if (!DialogService.Confirm("RESET ALL DATA",
                    "Every task, activity entry and focus session will be erased. This cannot be undone.", "ERASE"))
                return;

            _tasks.ReplaceAll(new System.Collections.ObjectModel.ObservableCollection<MissionTask>());
            _store.Data.FocusSessions.Clear();
            _activity.Clear();
            _activity.Log(ActivityKind.Data, "All data reset", "Command center returned to factory state");
            _store.SaveNow();

            _toasts.Show("DATA RESET", "Command center cleared.", ToastKind.Warning);
            Status = "All data erased.";
        }

        private void OpenFolder()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = _store.RootFolder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Status = "Could not open folder: " + ex.Message;
            }
        }

        private void Persist() => _store.RequestSave();
    }
}

using System;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using NEXUS.Common;
using NEXUS.Models;
using NEXUS.Services;

namespace NEXUS.ViewModels
{
    public class ActivityViewModel : ViewModelBase
    {
        private readonly ActivityService _activity;
        private readonly ListCollectionView _view;
        private string _filter = "ALL";
        private string _search = "";

        public ActivityViewModel(ActivityService activity)
        {
            _activity = activity;
            _view = new ListCollectionView(_activity.Entries) { Filter = Match };
            _activity.Changed += Refresh;

            ClearCommand = new RelayCommand(_ =>
            {
                if (DialogService.Confirm("CLEAR ACTIVITY LOG",
                        "All recorded activity will be removed. Tasks are not affected.", "CLEAR"))
                    _activity.Clear();
            });

            SetFilterCommand = new RelayCommand(p => Filter = p?.ToString() ?? "ALL");
        }

        public ICollectionView Entries => _view;
        public ICommand ClearCommand { get; }
        public ICommand SetFilterCommand { get; }

        public string Filter
        {
            get => _filter;
            set { if (Set(ref _filter, value)) Refresh(); }
        }

        public string Search
        {
            get => _search;
            set { if (Set(ref _search, value)) Refresh(); }
        }

        public int Count => _view.Count;
        public bool IsEmpty => _view.Count == 0;

        public override void OnActivated() => Refresh();

        private void Refresh()
        {
            _view.Refresh();
            OnPropertiesChanged(nameof(Count), nameof(IsEmpty));
        }

        private bool Match(object item)
        {
            if (item is not ActivityEntry e) return false;

            if (Filter != "ALL")
            {
                var group = Filter switch
                {
                    "TASKS" => e.Kind is ActivityKind.Created or ActivityKind.Completed or ActivityKind.Reopened
                                        or ActivityKind.Edited or ActivityKind.Deleted,
                    "STATUS" => e.Kind is ActivityKind.Status or ActivityKind.Priority,
                    "FOCUS" => e.Kind == ActivityKind.Focus,
                    "SYSTEM" => e.Kind is ActivityKind.System or ActivityKind.Data,
                    _ => true
                };
                if (!group) return false;
            }

            if (string.IsNullOrWhiteSpace(Search)) return true;
            var q = Search.Trim();
            return (e.Message?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                || (e.Detail?.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}

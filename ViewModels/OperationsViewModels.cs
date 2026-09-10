using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using NEXUS.Common;
using NEXUS.Models;
using NEXUS.Services;

namespace NEXUS.ViewModels
{
    public class AutomationsViewModel : ViewModelBase
    {
        private readonly AutomationEngine _engine;

        private string _newName = "";
        private TriggerType _newTrigger = TriggerType.DeviceOffline;
        private AutomationActionType _newAction = AutomationActionType.RaiseAlert;
        private string _newCondition = "";

        public AutomationsViewModel()
        {
            _engine = AppServices.Automations;
            _engine.Changed += Push;

            ToggleCommand = new RelayCommand(p => { if (p is AutomationRule r) _engine.Toggle(r); });
            DeleteCommand = new RelayCommand(p => Delete(p as AutomationRule));
            CreateCommand = new RelayCommand(_ => Create(), _ => !string.IsNullOrWhiteSpace(NewName));
        }

        public ObservableCollection<AutomationRule> Rules => _engine.Rules;

        public ICommand ToggleCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CreateCommand { get; }

        public IReadOnlyList<TriggerType> Triggers { get; } =
            Enum.GetValues(typeof(TriggerType)).Cast<TriggerType>().ToList();

        public IReadOnlyList<AutomationActionType> Actions { get; } =
            Enum.GetValues(typeof(AutomationActionType)).Cast<AutomationActionType>().ToList();

        public string NewName
        {
            get => _newName;
            set => Set(ref _newName, value);
        }

        public TriggerType NewTrigger
        {
            get => _newTrigger;
            set => Set(ref _newTrigger, value);
        }

        public AutomationActionType NewAction
        {
            get => _newAction;
            set => Set(ref _newAction, value);
        }

        public string NewCondition
        {
            get => _newCondition;
            set => Set(ref _newCondition, value);
        }

        public int EnabledCount => _engine.EnabledCount;
        public int TotalFired => _engine.TotalFired;
        public int RuleCount => Rules.Count;

        public override void OnActivated() => Push();

        public void SelectPayload(object payload) { }

        private void Create()
        {
            _engine.Add(new AutomationRule
            {
                Name = NewName.Trim(),
                Trigger = NewTrigger,
                Action = NewAction,
                Condition = NewCondition?.Trim() ?? ""
            });

            AppServices.Toasts.Show("AUTOMATION CREATED", NewName.Trim(), ToastKind.Success);
            NewName = "";
            NewCondition = "";
            Push();
        }

        private void Delete(AutomationRule rule)
        {
            if (rule == null) return;

            if (rule.IsBuiltIn)
            {
                AppServices.Toasts.Show("BUILT-IN RULE",
                    "Core rules can be disarmed but not deleted.", ToastKind.Warning);
                return;
            }

            if (!DialogService.Confirm("DELETE AUTOMATION",
                    "\"" + rule.Name + "\" will be removed from the rule engine.", "DELETE"))
                return;

            _engine.Remove(rule);
            Push();
        }

        private void Push()
            => OnPropertiesChanged(nameof(EnabledCount), nameof(TotalFired), nameof(RuleCount));
    }

    public class TerminalViewModel : ViewModelBase
    {
        private readonly TerminalService _terminal;
        private readonly List<string> _history = new List<string>();

        private string _input = "";
        private int _historyIndex = -1;

        public TerminalViewModel(ShellViewModel shell)
        {
            Shell = shell;
            _terminal = AppServices.Terminal;

            _terminal.ClearRequested += Clear;
            _terminal.NavigationRequested += key => Shell.Navigate(key);

            SubmitCommand = new RelayCommand(_ => Submit());
            ClearCommand = new RelayCommand(_ => Clear());
            HintCommand = new RelayCommand(p => { Input = p?.ToString() ?? ""; });

            Write("NEXUS TERMINAL \u00B7 " + Environment.MachineName, TerminalLineKind.Heading);
            Write("Type help for the command list. Every command reads live NEXUS state.", TerminalLineKind.Output);
        }

        public ShellViewModel Shell { get; }
        public ObservableCollection<TerminalLine> Lines { get; } = new ObservableCollection<TerminalLine>();

        public ICommand SubmitCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand HintCommand { get; }

        public IReadOnlyList<string> Suggestions { get; } = new[]
        {
            "status", "system", "network", "devices", "users", "servers", "services",
            "security", "alerts", "automations", "logs", "tasks", "help"
        };

        public string Input
        {
            get => _input;
            set => Set(ref _input, value);
        }

        public string Prompt => (AppServices.Users.Current?.Username ?? "operator") + "@nexus";

        public void Submit()
        {
            var text = Input?.Trim() ?? "";
            if (string.IsNullOrEmpty(text)) return;

            Write(Prompt + " > " + text, TerminalLineKind.Input);

            _history.Remove(text);
            _history.Add(text);
            _historyIndex = _history.Count;

            foreach (var line in _terminal.Execute(text)) Lines.Add(line);

            Lines.Add(new TerminalLine { Text = "", Kind = TerminalLineKind.Output });
            while (Lines.Count > 400) Lines.RemoveAt(0);

            Input = "";
            ScrollRequested?.Invoke();
        }

        /// <summary>Tab completion over the command list.</summary>
        public void Complete()
        {
            if (string.IsNullOrWhiteSpace(Input) || Input.Contains(" ")) return;
            Input = _terminal.Complete(Input);
        }

        public void HistoryBack()
        {
            if (_history.Count == 0) return;
            _historyIndex = Math.Max(0, _historyIndex - 1);
            Input = _history[_historyIndex];
        }

        public void HistoryForward()
        {
            if (_history.Count == 0) return;
            _historyIndex = Math.Min(_history.Count, _historyIndex + 1);
            Input = _historyIndex >= _history.Count ? "" : _history[_historyIndex];
        }

        public event Action ScrollRequested;

        private void Clear()
        {
            Lines.Clear();
            Write("NEXUS TERMINAL \u00B7 " + Environment.MachineName, TerminalLineKind.Heading);
        }

        private void Write(string text, TerminalLineKind kind)
            => Lines.Add(new TerminalLine { Text = text, Kind = kind });
    }

    /// <summary>The full NEXUS audit trail, across every subsystem.</summary>
    public class LogsViewModel : ViewModelBase
    {
        private readonly EventBus _bus;
        private readonly ListCollectionView _view;
        private string _filter = "ALL";
        private string _search = "";

        public LogsViewModel()
        {
            _bus = AppServices.Bus;
            _view = new ListCollectionView(_bus.Events) { Filter = Match };
            _bus.Published += _ => Refresh();

            SetFilterCommand = new RelayCommand(p => Filter = p?.ToString() ?? "ALL");
            ClearCommand = new RelayCommand(_ =>
            {
                if (DialogService.Confirm("CLEAR AUDIT TRAIL",
                        "Every recorded event will be removed. Subsystem state is not affected.", "CLEAR"))
                {
                    _bus.ClearEvents();
                    Refresh();
                }
            });
        }

        public ICollectionView Entries => _view;
        public ICommand SetFilterCommand { get; }
        public ICommand ClearCommand { get; }

        public IReadOnlyList<string> Filters { get; } = new[]
        {
            "ALL", "System", "Auth", "Access", "Device", "Network",
            "Server", "Service", "Security", "Automation", "Ticket", "Simulation", "Task", "File", "Terminal"
        };

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
        public int CriticalCount => _bus.Events.Count(e => e.Severity == EventSeverity.Critical);

        public override void OnActivated() => Refresh();

        public void SelectPayload(object payload) { }

        private void Refresh()
        {
            _view.Refresh();
            OnPropertiesChanged(nameof(Count), nameof(IsEmpty), nameof(CriticalCount));
        }

        private bool Match(object item)
        {
            if (item is not SystemEvent e) return false;

            if (Filter != "ALL" &&
                !string.Equals(e.Category.ToString(), Filter, StringComparison.OrdinalIgnoreCase))
                return false;

            if (string.IsNullOrWhiteSpace(Search)) return true;

            var q = Search.Trim();
            return (e.Message ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || (e.Detail ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || (e.Actor ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || (e.Source ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>Backs the Ctrl+Space command palette.</summary>
    public class SearchViewModel : ObservableObject
    {
        private readonly ShellViewModel _shell;
        private string _query = "";
        private SearchResult _selected;
        private bool _isOpen;

        public SearchViewModel(ShellViewModel shell)
        {
            _shell = shell;
            ChooseCommand = new RelayCommand(p => Choose(p as SearchResult ?? Selected));
            CloseCommand = new RelayCommand(_ => Close());
        }

        public ObservableCollection<SearchResult> Results { get; } = new ObservableCollection<SearchResult>();

        public ICommand ChooseCommand { get; }
        public ICommand CloseCommand { get; }

        public bool IsOpen
        {
            get => _isOpen;
            private set => Set(ref _isOpen, value);
        }

        public string Query
        {
            get => _query;
            set { if (Set(ref _query, value)) Run(); }
        }

        public SearchResult Selected
        {
            get => _selected;
            set => Set(ref _selected, value);
        }

        public bool HasResults => Results.Count > 0;
        public string Summary => Results.Count == 0
            ? (string.IsNullOrWhiteSpace(Query) ? "Search users, devices, servers, services, tasks, logs and sections"
                                                : "No matches")
            : Results.Count + " RESULT" + (Results.Count == 1 ? "" : "S");

        public void Open()
        {
            Query = "";
            Results.Clear();
            IsOpen = true;
            OnPropertiesChanged(nameof(HasResults), nameof(Summary));
        }

        public void Close()
        {
            IsOpen = false;
            Query = "";
            Results.Clear();
        }

        public void MoveSelection(int delta)
        {
            if (Results.Count == 0) return;

            var index = Selected == null ? -1 : Results.IndexOf(Selected);
            index += delta;
            if (index < 0) index = Results.Count - 1;
            if (index >= Results.Count) index = 0;
            Selected = Results[index];
        }

        private void Run()
        {
            Results.Clear();
            foreach (var r in AppServices.Search.Search(Query)) Results.Add(r);
            Selected = Results.FirstOrDefault();
            OnPropertiesChanged(nameof(HasResults), nameof(Summary));
        }

        private void Choose(SearchResult result)
        {
            if (result == null) return;
            Close();

            if (result.Invoke != null) { result.Invoke(); return; }
            _shell.OpenSearchResult(result);
        }
    }
}

using System.Collections.ObjectModel;
using System.Windows.Input;
using NEXUS.Common;

namespace NEXUS.ViewModels
{
    public class QuickAction
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string Glyph { get; set; }
        public string Shortcut { get; set; }
        public string BrushKey { get; set; } = "BrushAccent";
        public ICommand Command { get; set; }
        public object Parameter { get; set; }
    }

    /// <summary>Every tile here routes into a real command owned by the shell.</summary>
    public class QuickActionsViewModel : ViewModelBase
    {
        public QuickActionsViewModel(ShellViewModel shell)
        {
            Shell = shell;

            Actions.Add(new QuickAction
            {
                Title = "NEW TASK",
                Description = "Provision a new work item into the mission.",
                Glyph = "Add",
                Shortcut = "CTRL+N",
                BrushKey = "BrushAccent",
                Command = shell.NewTaskCommand
            });

            Actions.Add(new QuickAction
            {
                Title = "START FOCUS TIMER",
                Description = "Begin a timed session on the highest-priority open task.",
                Glyph = "Focus",
                Shortcut = "CTRL+F",
                BrushKey = "BrushLow",
                Command = shell.StartFocusCommand
            });

            Actions.Add(new QuickAction
            {
                Title = "VIEW ALL TASKS",
                Description = "Open the full task register with filters cleared.",
                Glyph = "Tasks",
                Shortcut = "CTRL+2",
                BrushKey = "BrushNormal",
                Command = shell.ShowTasksCommand,
                Parameter = "ALL"
            });

            Actions.Add(new QuickAction
            {
                Title = "TODAY",
                Description = "Show only work due before midnight.",
                Glyph = "Calendar",
                Shortcut = "",
                BrushKey = "BrushHigh",
                Command = shell.ShowTasksCommand,
                Parameter = "TODAY"
            });

            Actions.Add(new QuickAction
            {
                Title = "DEADLINE",
                Description = "Show everything landing on the mission deadline.",
                Glyph = "Activity",
                Shortcut = "",
                BrushKey = "BrushCritical",
                Command = shell.ShowTasksCommand,
                Parameter = "DEADLINE"
            });

            Actions.Add(new QuickAction
            {
                Title = "CRITICAL",
                Description = "Isolate critical-priority items still open.",
                Glyph = "Warning",
                Shortcut = "",
                BrushKey = "BrushCritical",
                Command = shell.ShowTasksCommand,
                Parameter = "CRITICAL"
            });

            Actions.Add(new QuickAction
            {
                Title = "COMPLETED",
                Description = "Review everything already cleared.",
                Glyph = "Check",
                Shortcut = "",
                BrushKey = "BrushCompleted",
                Command = shell.ShowTasksCommand,
                Parameter = "COMPLETED"
            });

            Actions.Add(new QuickAction
            {
                Title = "SETTINGS",
                Description = "Theme, notifications, deadline and data controls.",
                Glyph = "Settings",
                Shortcut = "CTRL+7",
                BrushKey = "BrushPlanned",
                Command = shell.ShowSettingsCommand
            });
        }

        public ShellViewModel Shell { get; }
        public ObservableCollection<QuickAction> Actions { get; } = new ObservableCollection<QuickAction>();
    }
}

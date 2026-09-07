using System;
using System.Windows.Input;
using NEXUS.Common;
using NEXUS.Models;
using NEXUS.Services;

namespace NEXUS.ViewModels
{
    /// <summary>
    /// One shared set of per-task commands so the overview, task list and focus view
    /// all behave identically. Bound as {Binding Commands.Xxx} from each section.
    /// </summary>
    public class TaskCommands
    {
        private readonly TaskService _tasks;
        private readonly ToastService _toasts;

        /// <summary>Wired by the shell so a task can hand itself to the focus timer.</summary>
        public Action<MissionTask> FocusRequested { get; set; }

        public TaskCommands(TaskService tasks, ToastService toasts)
        {
            _tasks = tasks;
            _toasts = toasts;

            ToggleCompleteCommand = new RelayCommand(p => { if (p is MissionTask t) _tasks.ToggleComplete(t); });
            CompleteCommand = new RelayCommand(p => { if (p is MissionTask t) _tasks.Complete(t); },
                                               p => p is MissionTask t && !t.IsCompleted);
            ReopenCommand = new RelayCommand(p => { if (p is MissionTask t) _tasks.Reopen(t); },
                                             p => p is MissionTask t && t.IsCompleted);
            EditCommand = new RelayCommand(p => { if (p is MissionTask t) Edit(t); });
            DeleteCommand = new RelayCommand(p => { if (p is MissionTask t) Delete(t); });
            FocusCommand = new RelayCommand(p => { if (p is MissionTask t) FocusRequested?.Invoke(t); });
            CyclePriorityCommand = new RelayCommand(p => { if (p is MissionTask t) CyclePriority(t); });
            SetStateCommand = new RelayCommand(SetState);
            ToggleSubTaskCommand = new RelayCommand(_ => _tasks.Touch());
        }

        public ICommand ToggleCompleteCommand { get; }
        public ICommand CompleteCommand { get; }
        public ICommand ReopenCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand FocusCommand { get; }
        public ICommand CyclePriorityCommand { get; }
        public ICommand SetStateCommand { get; }
        public ICommand ToggleSubTaskCommand { get; }

        private void Edit(MissionTask task)
        {
            var edited = DialogService.EditTask(task, "EDIT TASK");
            if (edited == null) return;

            var wasCompleted = task.IsCompleted;
            task.CopyFrom(edited);

            if (!wasCompleted && task.IsCompleted) _tasks.Complete(task);
            else _tasks.NotifyEdited(task);

            _toasts.Show("TASK UPDATED", task.Title, ToastKind.Info);
        }

        private void Delete(MissionTask task)
        {
            if (!DialogService.Confirm("DELETE TASK",
                    "\"" + task.Title + "\" will be permanently removed from the mission.", "DELETE"))
                return;

            _tasks.Delete(task);
            _toasts.Show("TASK DELETED", task.Title, ToastKind.Warning);
        }

        private void CyclePriority(MissionTask task)
        {
            var next = task.Priority switch
            {
                TaskPriority.Critical => TaskPriority.High,
                TaskPriority.High => TaskPriority.Normal,
                TaskPriority.Normal => TaskPriority.Low,
                _ => TaskPriority.Critical
            };
            _tasks.SetPriority(task, next);
        }

        /// <summary>Parameter arrives as "State|TaskId" from context menus, or a tuple from code.</summary>
        private void SetState(object parameter)
        {
            if (parameter is object[] pair && pair.Length == 2 &&
                pair[0] is MissionTask task && Enum.TryParse<TaskState>(pair[1]?.ToString(), out var state))
            {
                _tasks.SetState(task, state);
            }
        }

        public void SetState(MissionTask task, TaskState state) => _tasks.SetState(task, state);
    }
}

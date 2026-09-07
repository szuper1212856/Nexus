using System.Windows;
using NEXUS.Models;
using NEXUS.ViewModels;
using NEXUS.Views;

namespace NEXUS.Services
{
    /// <summary>
    /// Thin wrapper so view-models can raise modal windows without referencing them directly
    /// in their own logic. Owner is always the main window so dialogs centre correctly.
    /// </summary>
    public static class DialogService
    {
        private static Window Owner => Application.Current?.MainWindow;

        /// <summary>Opens the editor on a copy; returns the edited copy or null if cancelled.</summary>
        public static MissionTask EditTask(MissionTask task, string title)
        {
            var vm = new TaskEditorViewModel(task, title);
            var window = new TaskEditorWindow { DataContext = vm };
            if (Owner != null && Owner.IsLoaded) window.Owner = Owner;
            vm.CloseRequested += result => { window.DialogResult = result; };

            return window.ShowDialog() == true ? vm.Result : null;
        }

        public static bool Confirm(string heading, string message, string confirmLabel = "CONFIRM", bool destructive = true)
        {
            var window = new ConfirmWindow(heading, message, confirmLabel, destructive);
            if (Owner != null && Owner.IsLoaded) window.Owner = Owner;
            return window.ShowDialog() == true;
        }
    }
}

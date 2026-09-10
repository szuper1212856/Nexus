using System;
using System.Windows;
using System.Windows.Input;
using NEXUS.ViewModels;

namespace NEXUS.Views
{
    public partial class TaskEditorWindow : Window
    {
        public TaskEditorWindow()
        {
            InitializeComponent();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); } catch (InvalidOperationException) { }
        }

        /// <summary>Enter in the subtask box adds the entry without leaving the keyboard.</summary>
        private void SubTask_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (DataContext is not TaskEditorViewModel vm) return;

            if (vm.AddSubTaskCommand.CanExecute(null))
                vm.AddSubTaskCommand.Execute(null);

            e.Handled = true;
        }
    }
}

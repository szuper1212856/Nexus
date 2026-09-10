using System.Windows;
using System.Windows.Controls;
using NEXUS.ViewModels;

namespace NEXUS.Views
{
    public partial class FilesView : UserControl
    {
        public FilesView() => InitializeComponent();

        private void OnDragOver(object sender, DragEventArgs e)
        {
            var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
            DropHint.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
            e.Handled = true;
        }

        private void OnDragLeave(object sender, DragEventArgs e)
            => DropHint.Visibility = Visibility.Collapsed;

        private void OnFilesDropped(object sender, DragEventArgs e)
        {
            DropHint.Visibility = Visibility.Collapsed;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;

            (DataContext as FilesViewModel)?.ImportPaths(paths);
            e.Handled = true;
        }
    }
}

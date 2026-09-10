using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NEXUS.ViewModels;

namespace NEXUS.Views
{
    public partial class TerminalView : UserControl
    {
        private TerminalViewModel _bound;

        public TerminalView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (s, e) => InputBox.Focus();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_bound != null) _bound.ScrollRequested -= ScrollToEnd;
            _bound = DataContext as TerminalViewModel;
            if (_bound != null) _bound.ScrollRequested += ScrollToEnd;
        }

        private void ScrollToEnd()
            => Dispatcher.BeginInvoke(new System.Action(() => OutputScroll.ScrollToEnd()));

        private void OnInputKeyDown(object sender, KeyEventArgs e)
        {
            if (_bound == null) return;

            switch (e.Key)
            {
                case Key.Enter:
                    _bound.Submit();
                    e.Handled = true;
                    break;

                case Key.Tab:
                    _bound.Complete();
                    InputBox.CaretIndex = InputBox.Text.Length;
                    e.Handled = true;
                    break;

                case Key.Up:
                    _bound.HistoryBack();
                    InputBox.CaretIndex = InputBox.Text.Length;
                    e.Handled = true;
                    break;

                case Key.Down:
                    _bound.HistoryForward();
                    InputBox.CaretIndex = InputBox.Text.Length;
                    e.Handled = true;
                    break;
            }
        }
    }
}

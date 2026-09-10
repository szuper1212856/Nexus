using System.Windows;
using System.Windows.Media;

namespace NEXUS.Views
{
    public partial class ConfirmWindow : Window
    {
        public ConfirmWindow(string heading, string message, string confirmLabel, bool destructive)
        {
            InitializeComponent();

            HeadingText.Text = heading;
            MessageText.Text = message;
            ConfirmButton.Content = confirmLabel;

            // Non-destructive confirmations use the accent palette instead of the alert red.
            if (!destructive)
            {
                var accent = (Brush)Application.Current.Resources["BrushAccent"];
                IconHost.BorderBrush = accent;
                IconGlyph.Foreground = accent;
                IconGlyph.Text = "\uE946";
                ConfirmButton.Style = (Style)Application.Current.Resources["BtnPrimary"];
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}

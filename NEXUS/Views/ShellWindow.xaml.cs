using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using NEXUS.Services;
using NEXUS.ViewModels;

namespace NEXUS.Views
{
    public partial class ShellWindow : Window
    {
        private readonly ShellViewModel _vm;

        public ShellWindow()
        {
            InitializeComponent();

            _vm = new ShellViewModel();
            DataContext = _vm;

            _vm.PropertyChanged += OnShellPropertyChanged;
            _vm.FocusFinished += BringToFront;

            StateChanged += OnWindowStateChanged;
            Closing += OnClosing;
        }

        // ---------- window chrome ----------

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }

            // DragMove throws if the button was already released; ignoring is the standard guard.
            try { DragMove(); } catch (InvalidOperationException) { }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void ToggleMaximize()
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        /// <summary>
        /// WindowChrome lets a maximized window overhang the work area by the resize border,
        /// so the root border compensates with an equal margin.
        /// </summary>
        private void OnWindowStateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                var border = SystemParameters.WindowResizeBorderThickness;
                RootBorder.Margin = new Thickness(border.Left + 1, border.Top + 1, border.Right + 1, border.Bottom + 1);
                RootBorder.BorderThickness = new Thickness(0);
                MaxButton.Content = "\uE923";
                MaxButton.ToolTip = "Restore";
            }
            else
            {
                RootBorder.Margin = new Thickness(0);
                RootBorder.BorderThickness = new Thickness(1);
                MaxButton.Content = "\uE922";
                MaxButton.ToolTip = "Maximize";
            }
        }

        // ---------- transitions ----------

        private void OnShellPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ShellViewModel.CurrentViewModel))
                PlaySectionTransition();
        }

        /// <summary>Short fade and lift so switching sections reads as a deliberate change.</summary>
        private void PlaySectionTransition()
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var lift = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            SectionHost.BeginAnimation(OpacityProperty, fade);
            SectionShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, lift);
        }

        private void BringToFront()
        {
            try
            {
                if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
                Activate();
                Topmost = true;
                Topmost = false;
            }
            catch { }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            _vm.Shutdown();
            AppServices.Store.SaveNow();
        }
    }
}

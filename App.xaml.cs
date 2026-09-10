using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using NEXUS.Services;
using NEXUS.Views;

namespace NEXUS
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += OnUnhandledException;

            // Services must exist before any view resolves a binding.
            // Verify the icon font up front so every glyph resolves to something renderable.
            Controls.Glyphs.Initialize(TryFindResource("FontIcon") as System.Windows.Media.FontFamily);

            AppServices.Initialize();

            var window = new ShellWindow();
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try { AppServices.Store?.SaveNow(); } catch { }
            base.OnExit(e);
        }

        /// <summary>
        /// Keeps the app alive on unexpected errors and leaves a log next to the data file,
        /// so a single bad binding never costs the user their session.
        /// </summary>
        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                var path = Path.Combine(AppServices.Store?.RootFolder ?? Path.GetTempPath(), "error.log");
                File.AppendAllText(path,
                    DateTime.Now.ToString("s") + "  " + e.Exception + Environment.NewLine + Environment.NewLine);
            }
            catch { }

            MessageBox.Show("NEXUS hit an unexpected error and recovered.\n\n" + e.Exception.Message,
                "NEXUS", MessageBoxButton.OK, MessageBoxImage.Warning);

            e.Handled = true;
        }
    }
}

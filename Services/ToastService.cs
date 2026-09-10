using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;
using NEXUS.Common;

namespace NEXUS.Services
{
    public enum ToastKind { Info, Success, Warning, Critical }

    public class Toast : ObservableObject
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public ToastKind Kind { get; set; } = ToastKind.Info;
        public string Glyph => Kind switch
        {
            ToastKind.Success => "Check",
            ToastKind.Warning => "Warning",
            ToastKind.Critical => "Alert",
            _ => "Info"
        };
    }

    /// <summary>Transient on-screen notifications rendered by the shell overlay.</summary>
    public class ToastService
    {
        private readonly DataStore _store;
        public ObservableCollection<Toast> Items { get; } = new ObservableCollection<Toast>();

        public ToastService(DataStore store) => _store = store;

        public void Show(string title, string message = "", ToastKind kind = ToastKind.Info, int seconds = 4)
        {
            if (_store.Data.Settings != null && !_store.Data.Settings.ToastsEnabled) return;

            var toast = new Toast { Title = title, Message = message, Kind = kind };
            Items.Insert(0, toast);
            while (Items.Count > 4) Items.RemoveAt(Items.Count - 1);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                Dismiss(toast);
            };
            timer.Start();
        }

        public void Dismiss(Toast toast)
        {
            if (toast != null && Items.Contains(toast)) Items.Remove(toast);
        }
    }
}

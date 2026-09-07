using System;
using System.Collections.ObjectModel;
using System.Linq;
using NEXUS.Models;

namespace NEXUS.Services
{
    /// <summary>Append-only feed of everything that happens, newest first, capped to keep the file small.</summary>
    public class ActivityService
    {
        private const int MaxEntries = 600;
        private readonly DataStore _store;

        public ObservableCollection<ActivityEntry> Entries { get; } = new ObservableCollection<ActivityEntry>();

        public event Action Changed;

        public ActivityService(DataStore store)
        {
            _store = store;
            foreach (var e in _store.Data.Activity.OrderByDescending(a => a.TimestampUtc))
                Entries.Add(e);
        }

        public void Log(ActivityKind kind, string message, string detail = "")
        {
            var entry = new ActivityEntry
            {
                Kind = kind,
                Message = message ?? "",
                Detail = detail ?? ""
            };

            Entries.Insert(0, entry);
            while (Entries.Count > MaxEntries) Entries.RemoveAt(Entries.Count - 1);

            SyncToStore();
            Changed?.Invoke();
            _store.RequestSave();
        }

        public void Clear()
        {
            Entries.Clear();
            SyncToStore();
            Changed?.Invoke();
            _store.RequestSave();
        }

        public void Reload()
        {
            Entries.Clear();
            foreach (var e in _store.Data.Activity.OrderByDescending(a => a.TimestampUtc))
                Entries.Add(e);
            Changed?.Invoke();
        }

        private void SyncToStore()
        {
            _store.Data.Activity.Clear();
            _store.Data.Activity.AddRange(Entries);
        }
    }
}

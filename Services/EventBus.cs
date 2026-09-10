using System;
using System.Collections.ObjectModel;
using System.Linq;
using NEXUS.Models;

namespace NEXUS.Services
{
    /// <summary>
    /// The spine of NEXUS. Every manager publishes here, and the audit log, alert
    /// list and automation engine all read from it. Nothing is ever written to this
    /// log unless real application activity produced it.
    /// </summary>
    public class EventBus
    {
        private const int MaxEvents = 1200;
        private readonly DataStore _store;

        public EventBus(DataStore store)
        {
            _store = store;

            foreach (var e in _store.Data.Events.OrderByDescending(e => e.TimestampUtc))
                Events.Add(e);
            foreach (var a in _store.Data.Alerts.OrderByDescending(a => a.RaisedUtc))
                Alerts.Add(a);
        }

        public ObservableCollection<SystemEvent> Events { get; } = new ObservableCollection<SystemEvent>();
        public ObservableCollection<Alert> Alerts { get; } = new ObservableCollection<Alert>();

        /// <summary>Raised for every published event, after it lands in the log.</summary>
        public event Action<SystemEvent> Published;

        /// <summary>Raised for every automation signal, consumed by the automation engine.</summary>
        public event Action<NexusSignal> Signalled;

        public event Action AlertsChanged;

        /// <summary>The identity attributed to actions taken from this console.</summary>
        public string CurrentActor { get; set; } = "SYSTEM";

        public int UnacknowledgedAlerts => Alerts.Count(a => !a.Acknowledged);

        public SystemEvent Publish(EventCategory category, string message, string detail = "",
                                   EventSeverity severity = EventSeverity.Info, string source = "", string actor = null)
        {
            var entry = new SystemEvent
            {
                Category = category,
                Severity = severity,
                Message = message ?? "",
                Detail = detail ?? "",
                Source = source ?? "",
                Actor = actor ?? CurrentActor
            };

            Events.Insert(0, entry);
            while (Events.Count > MaxEvents) Events.RemoveAt(Events.Count - 1);

            SyncEvents();
            Published?.Invoke(entry);
            _store.RequestSave();
            return entry;
        }

        /// <summary>Fires an automation trigger. Kept separate so logging and rules stay decoupled.</summary>
        public void Signal(TriggerType trigger, string subject, string detail = "", object payload = null)
            => Signalled?.Invoke(new NexusSignal
            {
                Trigger = trigger,
                Subject = subject ?? "",
                Detail = detail ?? "",
                Payload = payload
            });

        public Alert RaiseAlert(string title, string detail, EventSeverity severity, string source)
        {
            var alert = new Alert
            {
                Title = title ?? "",
                Detail = detail ?? "",
                Severity = severity,
                Source = source ?? ""
            };

            Alerts.Insert(0, alert);
            while (Alerts.Count > 60) Alerts.RemoveAt(Alerts.Count - 1);

            SyncAlerts();
            AlertsChanged?.Invoke();
            _store.RequestSave();
            return alert;
        }

        public void Acknowledge(Alert alert)
        {
            if (alert == null || alert.Acknowledged) return;
            alert.Acknowledged = true;
            SyncAlerts();
            AlertsChanged?.Invoke();
            Publish(EventCategory.Security, "Alert acknowledged", alert.Title, EventSeverity.Info, alert.Source);
        }

        public void AcknowledgeAll()
        {
            var changed = false;
            foreach (var a in Alerts)
                if (!a.Acknowledged) { a.Acknowledged = true; changed = true; }

            if (!changed) return;
            SyncAlerts();
            AlertsChanged?.Invoke();
            Publish(EventCategory.Security, "All alerts acknowledged", "", EventSeverity.Info, "SECURITY");
        }

        public void ClearEvents()
        {
            Events.Clear();
            SyncEvents();
            _store.RequestSave();
        }

        public void Reload()
        {
            Events.Clear();
            foreach (var e in _store.Data.Events.OrderByDescending(e => e.TimestampUtc)) Events.Add(e);

            Alerts.Clear();
            foreach (var a in _store.Data.Alerts.OrderByDescending(a => a.RaisedUtc)) Alerts.Add(a);

            AlertsChanged?.Invoke();
        }

        private void SyncEvents()
        {
            _store.Data.Events.Clear();
            _store.Data.Events.AddRange(Events);
        }

        private void SyncAlerts()
        {
            _store.Data.Alerts.Clear();
            _store.Data.Alerts.AddRange(Alerts);
        }
    }
}

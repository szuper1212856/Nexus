using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NEXUS.Models;

namespace NEXUS.Services
{
    /// <summary>
    /// Watches the event bus for signals and applies matching rules.
    /// WHEN a trigger fires, IF the condition matches the subject, THEN the action runs.
    /// </summary>
    public class AutomationEngine
    {
        private readonly DataStore _store;
        private readonly EventBus _bus;
        private readonly UserManager _users;
        private readonly ServerManager _servers;
        private readonly DeviceManager _devices;
        private readonly ToastService _toasts;

        public AutomationEngine(DataStore store, EventBus bus, UserManager users,
                                ServerManager servers, DeviceManager devices, ToastService toasts)
        {
            _store = store;
            _bus = bus;
            _users = users;
            _servers = servers;
            _devices = devices;
            _toasts = toasts;

            _bus.Signalled += OnSignal;
        }

        public ObservableCollection<AutomationRule> Rules => _store.Data.Automations;

        public event Action Changed;

        public int EnabledCount => Rules.Count(r => r.IsEnabled);
        public int TotalFired => Rules.Sum(r => r.TimesFired);

        public void Add(AutomationRule rule)
        {
            if (rule == null) return;
            Rules.Add(rule);
            _bus.Publish(EventCategory.Automation, "Automation created", rule.Name,
                         EventSeverity.Notice, "AUTOMATIONS");
            Touch();
        }

        public void Remove(AutomationRule rule)
        {
            if (rule == null || rule.IsBuiltIn || !Rules.Contains(rule)) return;
            Rules.Remove(rule);
            _bus.Publish(EventCategory.Automation, "Automation deleted", rule.Name,
                         EventSeverity.Warning, "AUTOMATIONS");
            Touch();
        }

        public void Toggle(AutomationRule rule)
        {
            if (rule == null) return;
            rule.IsEnabled = !rule.IsEnabled;
            _bus.Publish(EventCategory.Automation,
                rule.IsEnabled ? "Automation armed" : "Automation disabled", rule.Name,
                EventSeverity.Notice, "AUTOMATIONS");
            Touch();
        }

        private void OnSignal(NexusSignal signal)
        {
            if (signal == null) return;

            foreach (var rule in Rules.ToList())
            {
                if (!rule.IsEnabled || rule.Trigger != signal.Trigger) continue;

                if (!string.IsNullOrWhiteSpace(rule.Condition) &&
                    (signal.Subject ?? "").IndexOf(rule.Condition, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Fire(rule, signal);
            }
        }

        private void Fire(AutomationRule rule, NexusSignal signal)
        {
            rule.TimesFired = rule.TimesFired + 1;
            rule.LastFiredUtc = DateTime.UtcNow;

            switch (rule.Action)
            {
                case AutomationActionType.RaiseAlert:
                    _bus.RaiseAlert(rule.Name,
                        signal.Subject + (string.IsNullOrWhiteSpace(signal.Detail) ? "" : " \u00B7 " + signal.Detail),
                        EventSeverity.Warning, "AUTOMATION");
                    break;

                case AutomationActionType.Notify:
                    _toasts.Show("AUTOMATION TRIGGERED", rule.Name + " \u00B7 " + signal.Subject, ToastKind.Info);
                    break;

                case AutomationActionType.RevokeSessions:
                    if (signal.Payload is NexusUser user) _users.RevokeSessions(user);
                    break;

                case AutomationActionType.MarkDeviceOffline:
                    if (signal.Payload is Device device) device.Status = DeviceStatus.Offline;
                    break;

                case AutomationActionType.RestartService:
                    var target = _servers.FindService(
                        string.IsNullOrWhiteSpace(rule.ActionParameter) ? signal.Subject : rule.ActionParameter);
                    if (target != null) _servers.RestartService(target);
                    break;

                case AutomationActionType.PinFile:
                    if (signal.Payload is NexusFile pinTarget) pinTarget.IsPinned = true;
                    break;

                case AutomationActionType.CreateTask:
                    // Handled by the shell, which owns the task service.
                    TaskRequested?.Invoke(rule, signal);
                    break;
            }

            _bus.Publish(EventCategory.Automation, "Automation triggered",
                rule.Name + " \u00B7 " + rule.TriggerLabel + " \u2192 " + rule.ActionLabel +
                (string.IsNullOrWhiteSpace(signal.Subject) ? "" : " \u00B7 " + signal.Subject),
                EventSeverity.Notice, "AUTOMATIONS");

            Touch();
        }

        /// <summary>Raised when a rule wants a task created; the shell wires this to TaskService.</summary>
        public event Action<AutomationRule, NexusSignal> TaskRequested;

        public void Touch()
        {
            Changed?.Invoke();
            _store.RequestSave();
        }

        public void SeedDefaults()
        {
            void Add(string name, TriggerType trigger, AutomationActionType action, string condition = "")
                => Rules.Add(new AutomationRule
                {
                    Name = name,
                    Trigger = trigger,
                    Action = action,
                    Condition = condition,
                    IsBuiltIn = true
                });

            Add("Alert on server loss", TriggerType.ServerOffline, AutomationActionType.RaiseAlert);
            Add("Revoke sessions on suspension", TriggerType.UserSuspended, AutomationActionType.RevokeSessions);
            Add("Alert on device dropout", TriggerType.DeviceOffline, AutomationActionType.RaiseAlert);
            Add("Notify on service stop", TriggerType.ServiceStopped, AutomationActionType.Notify);
            Add("Audit permission changes", TriggerType.PermissionChanged, AutomationActionType.RaiseAlert);
            Add("Log file uploads", TriggerType.FileUploaded, AutomationActionType.LogEvent);

            _bus.Publish(EventCategory.Automation, "Automation rules provisioned",
                Rules.Count + " rules armed", EventSeverity.Notice, "AUTOMATIONS");
            Touch();
        }
    }

    /// <summary>Read model over the event bus and user directory for the security console.</summary>
    public class SecurityManager
    {
        private readonly EventBus _bus;
        private readonly UserManager _users;

        public SecurityManager(EventBus bus, UserManager users)
        {
            _bus = bus;
            _users = users;
        }

        public int OpenAlerts => _bus.UnacknowledgedAlerts;
        public int CriticalEvents => _bus.Events.Count(e =>
            e.Severity == EventSeverity.Critical && e.TimestampUtc > DateTime.UtcNow.AddHours(-24));
        public int SuspendedUsers => _users.SuspendedCount;
        public int ActiveSessions => _users.SessionCount;

        public string PostureLabel
        {
            get
            {
                if (OpenAlerts > 0 && CriticalEvents > 0) return "ELEVATED";
                if (OpenAlerts > 0 || SuspendedUsers > 0) return "GUARDED";
                return "NORMAL";
            }
        }

        public string PostureBrushKey => PostureLabel switch
        {
            "ELEVATED" => "BrushCritical",
            "GUARDED" => "BrushHigh",
            _ => "BrushCompleted"
        };

        public string PostureDetail => PostureLabel switch
        {
            "ELEVATED" => "Unacknowledged alerts alongside critical events in the last 24 hours.",
            "GUARDED" => "Open alerts or suspended accounts require review.",
            _ => "No open alerts. No suspended accounts. Audit trail clean."
        };

        public IEnumerable<SystemEvent> AuthEvents()
            => _bus.Events.Where(e => e.Category == EventCategory.Auth ||
                                      e.Category == EventCategory.Access ||
                                      e.Category == EventCategory.Security);
    }
}

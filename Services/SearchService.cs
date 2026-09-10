using System;
using System.Collections.Generic;
using System.Linq;
using NEXUS.Models;

namespace NEXUS.Services
{
    public class SearchResult
    {
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Category { get; set; } = "";
        public string Glyph { get; set; } = "Search";
        public string BrushKey { get; set; } = "BrushAccent";

        /// <summary>Section key the shell should navigate to when this result is chosen.</summary>
        public string SectionKey { get; set; } = "";

        /// <summary>The underlying entity, so the target section can select it.</summary>
        public object Payload { get; set; }

        public int Rank { get; set; }

        /// <summary>When set, choosing this result runs an action instead of navigating.</summary>
        public Action Invoke { get; set; }

        public bool IsCommand => Invoke != null;
    }

    /// <summary>Single index across every NEXUS subsystem, driving the Ctrl+Space palette.</summary>
    public class SearchService
    {
        private readonly DeviceManager _devices;
        private readonly UserManager _users;
        private readonly ServerManager _servers;
        private readonly TaskService _tasks;
        private readonly AutomationEngine _automations;
        private readonly FileVault _files;
        private readonly SimulationEngine _sim;
        private readonly EventBus _bus;

        /// <summary>Verb-style entries contributed by the shell, e.g. "Run system check".</summary>
        public List<SearchResult> Commands { get; } = new List<SearchResult>();

        public SearchService(DeviceManager devices, UserManager users, ServerManager servers,
                             TaskService tasks, AutomationEngine automations, FileVault files,
                             SimulationEngine sim, EventBus bus)
        {
            _devices = devices;
            _users = users;
            _servers = servers;
            _tasks = tasks;
            _automations = automations;
            _files = files;
            _sim = sim;
            _bus = bus;
        }

        public void RegisterCommand(string title, string subtitle, string glyph, Action action)
            => Commands.Add(new SearchResult
            {
                Title = title,
                Subtitle = subtitle,
                Category = "COMMAND",
                Glyph = glyph,
                BrushKey = "BrushAccent",
                Invoke = action
            });

        public List<SearchResult> Search(string query, int limit = 24)
        {
            var results = new List<SearchResult>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            var q = query.Trim();

            void Add(string title, string subtitle, string category, string glyph,
                     string brush, string section, object payload)
            {
                var rank = Rank(title, q);
                if (rank < 0) rank = Rank(subtitle, q);
                if (rank < 0) return;

                results.Add(new SearchResult
                {
                    Title = title,
                    Subtitle = subtitle,
                    Category = category,
                    Glyph = glyph,
                    BrushKey = brush,
                    SectionKey = section,
                    Payload = payload,
                    Rank = rank
                });
            }

            foreach (var d in _devices.Devices)
                Add(d.Name, d.TypeLabel + " \u00B7 " + d.StatusLabel + " \u00B7 " + d.AddressDisplay,
                    "DEVICE", d.Glyph, d.StatusBrushKey, "DEVICES", d);

            foreach (var u in _users.Users)
                Add(u.DisplayName, u.RoleLabel + " \u00B7 " + u.StatusLabel + " \u00B7 " + u.Username,
                    "USER", "Users", u.RoleBrushKey, "USERS", u);

            foreach (var s in _servers.Servers)
            {
                Add(s.Name, "Server \u00B7 " + s.StatusLabel + " \u00B7 " + s.ServiceSummary,
                    "SERVER", "Server", s.StatusBrushKey, "SERVERS", s);

                foreach (var svc in s.Services)
                    Add(svc.Name, "Service on " + s.Name + " \u00B7 " + svc.StateLabel,
                        "SERVICE", "Automation", svc.StateBrushKey, "SERVERS", svc);
            }

            foreach (var t in _tasks.Tasks)
                Add(t.Title, t.StateLabel + " \u00B7 " + t.PriorityLabel + " \u00B7 " + t.DeadlineDisplay,
                    "TASK", "Tasks", "BrushNormal", "TASKS", t);

            foreach (var r in _automations.Rules)
                Add(r.Name, "WHEN " + r.TriggerLabel + " THEN " + r.ActionLabel,
                    "AUTOMATION", "Automation", r.StateBrushKey, "AUTOMATIONS", r);

            foreach (var e in _bus.Events.Take(250))
                Add(e.Message, e.TimeDisplay + " \u00B7 " + e.CategoryLabel +
                    (string.IsNullOrEmpty(e.Detail) ? "" : " \u00B7 " + e.Detail),
                    "EVENT", "Log", e.CategoryBrushKey, "LOGS", e);

            foreach (var e in _sim.Employees)
                Add(e.FullName, e.JobTitle + " \u00B7 " + e.DepartmentLabel + " \u00B7 " + e.Username,
                    "EMPLOYEE", "Users", e.AccountBrushKey, "DIRECTORY", e);

            foreach (var t in _sim.Tickets)
                Add(t.Reference + " \u00B7 " + t.Title,
                    t.StatusLabel + " \u00B7 " + t.PriorityLabel + " \u00B7 " + t.RequesterUsername,
                    "TICKET", "Tasks", t.PriorityBrushKey, "SERVICEDESK", t);

            foreach (var f in _files.Active)
                Add(f.Name, f.TypeLabel + " \u00B7 " + f.SizeDisplay + " \u00B7 " + f.ModifiedDisplay,
                    f.IsDocument ? "DOCUMENT" : "FILE", f.Glyph, "BrushLow",
                    f.IsDocument ? "DOCUMENTS" : "FILES", f);

            foreach (var command in Commands)
            {
                var rank = Rank(command.Title, q);
                if (rank < 0) rank = Rank(command.Subtitle, q);
                if (rank < 0) continue;

                results.Add(new SearchResult
                {
                    Title = command.Title,
                    Subtitle = command.Subtitle,
                    Category = command.Category,
                    Glyph = command.Glyph,
                    BrushKey = command.BrushKey,
                    Invoke = command.Invoke,
                    // Verbs outrank records so "restart jellyfin" lands on the action.
                    Rank = Math.Max(0, rank - 1)
                });
            }

            foreach (var section in Sections)
                Add(section.Item1, section.Item2, "SECTION", section.Item3, "BrushAccent", section.Item4, null);

            return results.OrderBy(r => r.Rank)
                          .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                          .Take(limit)
                          .ToList();
        }

        private static readonly List<Tuple<string, string, string, string>> Sections =
            new List<Tuple<string, string, string, string>>
            {
                Tuple.Create("Command Center", "Live operations console", "Command", "COMMAND"),
                Tuple.Create("Devices", "Device inventory and controls", "Devices", "DEVICES"),
                Tuple.Create("Network", "Topology and interfaces", "Network", "NETWORK"),
                Tuple.Create("Servers", "Servers and services", "Server", "SERVERS"),
                Tuple.Create("Security", "Posture, sessions, audit", "Security", "SECURITY"),
                Tuple.Create("Users", "Account directory", "Users", "USERS"),
                Tuple.Create("Access Control", "Permission matrix", "Access", "ACCESS"),
                Tuple.Create("Automations", "Rule engine", "Automation", "AUTOMATIONS"),
                Tuple.Create("Terminal", "Command console", "Terminal", "TERMINAL"),
                Tuple.Create("Logs", "Full audit trail", "Log", "LOGS"),
                Tuple.Create("System", "Local machine telemetry", "System", "SYSTEM"),
                Tuple.Create("Tasks", "Task register", "Tasks", "TASKS"),
                Tuple.Create("Service Desk", "Ticket queue and incidents", "Tasks", "SERVICEDESK"),
                Tuple.Create("Directory", "Employees and accounts", "Users", "DIRECTORY"),
                Tuple.Create("Files", "File library and vault", "Files", "FILES"),
                Tuple.Create("Documents", "Document workspace", "Documents", "DOCUMENTS"),
                Tuple.Create("Settings", "Configuration and data", "Settings", "SETTINGS")
            };

        /// <summary>Lower is better: 0 = prefix match, 1 = word start, 2 = contains, -1 = no match.</summary>
        private static int Rank(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack)) return -1;

            var index = haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            if (index < 0) return -1;
            if (index == 0) return 0;
            return char.IsWhiteSpace(haystack[index - 1]) ? 1 : 2;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NEXUS.Models;

namespace NEXUS.Services
{
    public enum TerminalLineKind { Input, Output, Success, Warning, Error, Heading }

    public class TerminalLine
    {
        public string Text { get; set; } = "";
        public TerminalLineKind Kind { get; set; } = TerminalLineKind.Output;

        public string BrushKey => Kind switch
        {
            TerminalLineKind.Input => "BrushAccent",
            TerminalLineKind.Success => "BrushCompleted",
            TerminalLineKind.Warning => "BrushHigh",
            TerminalLineKind.Error => "BrushCritical",
            TerminalLineKind.Heading => "BrushTextMuted",
            _ => "BrushTextDim"
        };
    }

    /// <summary>
    /// Parses and executes NEXUS console commands. Every command reads or mutates real
    /// application state — there are no decorative responses.
    /// </summary>
    public class TerminalService
    {
        private readonly DeviceManager _devices;
        private readonly NetworkManager _network;
        private readonly UserManager _users;
        private readonly AccessManager _access;
        private readonly ServerManager _servers;
        private readonly SecurityManager _security;
        private readonly AutomationEngine _automations;
        private readonly SystemInfoService _sysinfo;
        private readonly TaskService _tasks;
        private readonly EventBus _bus;

        public TerminalService(DeviceManager devices, NetworkManager network, UserManager users,
                               AccessManager access, ServerManager servers, SecurityManager security,
                               AutomationEngine automations, SystemInfoService sysinfo,
                               TaskService tasks, EventBus bus)
        {
            _devices = devices;
            _network = network;
            _users = users;
            _access = access;
            _servers = servers;
            _security = security;
            _automations = automations;
            _sysinfo = sysinfo;
            _tasks = tasks;
            _bus = bus;
        }

        public static readonly string[] Commands =
        {
            "help", "system", "network", "devices", "device", "users", "user", "access",
            "servers", "services", "service", "security", "alerts", "logs", "tasks",
            "automations", "ping", "scan", "whoami", "status", "launch", "clear"
        };

        /// <summary>Raised when a command asks the shell to do something UI-level.</summary>
        public event Action<string> NavigationRequested;
        public event Action ClearRequested;

        public IReadOnlyList<TerminalLine> Execute(string input)
        {
            var lines = new List<TerminalLine>();
            if (string.IsNullOrWhiteSpace(input)) return lines;

            var parts = input.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            var arg = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "";

            void Out(string text, TerminalLineKind kind = TerminalLineKind.Output)
                => lines.Add(new TerminalLine { Text = text, Kind = kind });

            switch (cmd)
            {
                case "help":
                    Out("AVAILABLE COMMANDS", TerminalLineKind.Heading);
                    Out("  system                  local machine telemetry");
                    Out("  network                 interfaces, gateway, throughput");
                    Out("  devices                 list the device inventory");
                    Out("  device <name>           inspect one device");
                    Out("  users                   list accounts");
                    Out("  user <name>             inspect one account");
                    Out("  access <name>           show granted permissions");
                    Out("  servers                 list servers and load");
                    Out("  services                list every service");
                    Out("  service <name>          inspect one service");
                    Out("  security                security posture summary");
                    Out("  alerts                  open alerts");
                    Out("  logs [n]                last n audit entries (default 12)");
                    Out("  tasks                   task subsystem summary");
                    Out("  automations             configured rules");
                    Out("  ping <host>             reachability check");
                    Out("  scan                    read this machine's ARP neighbours");
                    Out("  status                  one-line status of everything");
                    Out("  whoami                  current operator");
                    Out("  launch <section>        open a NEXUS section");
                    Out("  clear                   clear the console");
                    break;

                case "clear":
                    ClearRequested?.Invoke();
                    return lines;

                case "whoami":
                    var me = _users.Current;
                    if (me == null) { Out("No operator signed in.", TerminalLineKind.Warning); break; }
                    Out(me.DisplayName + "  [" + me.Username + "]", TerminalLineKind.Success);
                    Out("  role         " + me.RoleLabel);
                    Out("  access       " + me.AccessLevel);
                    Out("  permissions  " + me.Granted.Count + " of " + Permissions.All.Length);
                    Out("  sessions     " + me.Sessions.Count);
                    break;

                case "status":
                    Out("NEXUS STATUS", TerminalLineKind.Heading);
                    Out("  system     OPERATIONAL", TerminalLineKind.Success);
                    Out("  network    " + _network.StatusLabel + "  " + _network.PrimaryAddress);
                    Out("  devices    " + _devices.OnlineCount + " online / " + _devices.OfflineCount + " offline");
                    Out("  servers    " + _servers.OnlineCount + " online");
                    Out("  services   " + _servers.RunningServices + " running of " + _servers.TotalServices);
                    Out("  users      " + _users.ActiveCount + " active, " + _users.SessionCount + " sessions");
                    Out("  security   " + _security.PostureLabel);
                    Out("  alerts     " + _bus.UnacknowledgedAlerts + " open");
                    Out("  tasks      " + _tasks.Remaining + " open of " + _tasks.Total);
                    break;

                case "system":
                    Out("LOCAL MACHINE", TerminalLineKind.Heading);
                    Out("  host         " + _sysinfo.MachineName);
                    Out("  user         " + _sysinfo.UserName);
                    Out("  os           " + _sysinfo.OperatingSystem);
                    Out("  arch         " + _sysinfo.Architecture + "  (" + _sysinfo.LogicalProcessors + " logical cores)");
                    Out("  runtime      " + _sysinfo.Runtime);
                    Out("  cpu          " + _sysinfo.CpuPercent);
                    Out("  memory       " + _sysinfo.UsedMemoryDisplay + " / " + _sysinfo.TotalMemoryDisplay +
                        "  (" + _sysinfo.MemoryPercent + ")");
                    Out("  uptime       " + _sysinfo.UptimeDisplay);
                    foreach (var v in _sysinfo.Volumes)
                        Out("  volume " + v.Name.PadRight(5) + v.UsedDisplay + " / " + v.TotalDisplay + "  (" + v.UsedPercent + ")");
                    break;

                case "network":
                    Out("NETWORK", TerminalLineKind.Heading);
                    Out("  state        " + _network.StatusLabel,
                        _network.IsConnected ? TerminalLineKind.Success : TerminalLineKind.Error);
                    Out("  host         " + _network.HostName);
                    Out("  address      " + _network.PrimaryAddress + " via " + _network.PrimaryInterface);
                    Out("  gateway      " + _network.Gateway);
                    Out("  throughput   " + _network.ThroughputDisplay);
                    Out("  interfaces", TerminalLineKind.Heading);
                    foreach (var n in _network.Interfaces.Where(i => i.IsUp))
                        Out("    " + n.Name.PadRight(24) + n.IpAddress.PadRight(16) + n.LinkLabel);
                    break;

                case "devices":
                    Out("DEVICE INVENTORY  (" + _devices.Total + ")", TerminalLineKind.Heading);
                    foreach (var d in _devices.Devices)
                        Out("  " + (d.IsOnline ? "\u25CF " : "\u25CB ") + d.Name.PadRight(24)
                            + d.TypeLabel.PadRight(14) + d.AddressDisplay.PadRight(16) + d.IntegrationLabel,
                            d.IsOnline ? TerminalLineKind.Output : TerminalLineKind.Warning);
                    break;

                case "device":
                    if (string.IsNullOrWhiteSpace(arg)) { Out("Usage: device <name>", TerminalLineKind.Error); break; }
                    var dev = _devices.Find(arg);
                    if (dev == null) { Out("No device matches \"" + arg + "\".", TerminalLineKind.Error); break; }
                    Out(dev.Name.ToUpperInvariant(), TerminalLineKind.Heading);
                    Out("  status       " + dev.StatusLabel, dev.IsOnline ? TerminalLineKind.Success : TerminalLineKind.Warning);
                    Out("  type         " + dev.TypeLabel);
                    Out("  connection   " + dev.LinkLabel);
                    Out("  address      " + dev.AddressDisplay);
                    Out("  mac          " + (string.IsNullOrEmpty(dev.MacAddress) ? "\u2014" : dev.MacAddress));
                    Out("  last seen    " + dev.LastSeenDisplay);
                    Out("  integration  " + dev.IntegrationLabel);
                    if (!string.IsNullOrEmpty(dev.IntegrationNotice))
                        Out("  note         " + dev.IntegrationNotice, TerminalLineKind.Warning);
                    break;

                case "users":
                    Out("ACCOUNTS  (" + _users.Users.Count + ")", TerminalLineKind.Heading);
                    foreach (var u in _users.Users)
                        Out("  " + u.Username.PadRight(14) + u.DisplayName.PadRight(20)
                            + u.RoleLabel.PadRight(16) + u.StatusLabel.PadRight(12)
                            + u.Granted.Count + " perms",
                            u.IsSuspended ? TerminalLineKind.Error : TerminalLineKind.Output);
                    break;

                case "user":
                    if (string.IsNullOrWhiteSpace(arg)) { Out("Usage: user <name>", TerminalLineKind.Error); break; }
                    var usr = _users.Find(arg);
                    if (usr == null) { Out("No account matches \"" + arg + "\".", TerminalLineKind.Error); break; }
                    Out(usr.DisplayName.ToUpperInvariant(), TerminalLineKind.Heading);
                    Out("  username     " + usr.Username);
                    Out("  role         " + usr.RoleLabel);
                    Out("  access       " + usr.AccessLevel);
                    Out("  status       " + usr.StatusLabel,
                        usr.IsSuspended ? TerminalLineKind.Error : TerminalLineKind.Success);
                    Out("  last login   " + usr.LastLoginDisplay);
                    Out("  sessions     " + usr.Sessions.Count);
                    Out("  permissions  " + usr.Granted.Count + " of " + Permissions.All.Length);
                    break;

                case "access":
                    if (string.IsNullOrWhiteSpace(arg)) { Out("Usage: access <name>", TerminalLineKind.Error); break; }
                    var subject = _users.Find(arg);
                    if (subject == null) { Out("No account matches \"" + arg + "\".", TerminalLineKind.Error); break; }
                    Out("PERMISSIONS \u00B7 " + subject.DisplayName.ToUpperInvariant(), TerminalLineKind.Heading);
                    foreach (var p in Permissions.All)
                    {
                        var has = subject.Granted.Contains(p);
                        Out("  " + (has ? "\u2713 " : "\u2013 ") + Permissions.Label(p),
                            has ? TerminalLineKind.Success : TerminalLineKind.Output);
                    }
                    break;

                case "servers":
                    Out("SERVERS  (" + _servers.Servers.Count + ")", TerminalLineKind.Heading);
                    foreach (var s in _servers.Servers)
                        Out("  " + (s.IsOnline ? "\u25CF " : "\u25CB ") + s.Name.PadRight(22)
                            + ("CPU " + s.CpuPercent).PadRight(10)
                            + ("MEM " + s.MemoryPercent).PadRight(10)
                            + s.ServiceSummary.PadRight(16) + "UP " + s.UptimeDisplay,
                            s.IsOnline ? TerminalLineKind.Output : TerminalLineKind.Error);
                    break;

                case "services":
                    Out("SERVICES", TerminalLineKind.Heading);
                    foreach (var s in _servers.Servers)
                        foreach (var svc in s.Services)
                            Out("  " + (svc.IsRunning ? "\u25CF " : "\u25CB ") + svc.Name.PadRight(20)
                                + s.Name.PadRight(20) + svc.StateLabel.PadRight(12)
                                + svc.PortDisplay.PadRight(8) + svc.UptimeDisplay,
                                svc.IsRunning ? TerminalLineKind.Output : TerminalLineKind.Warning);
                    break;

                case "service":
                    if (string.IsNullOrWhiteSpace(arg)) { Out("Usage: service <name>", TerminalLineKind.Error); break; }
                    var found = _servers.FindService(arg);
                    if (found == null) { Out("No service matches \"" + arg + "\".", TerminalLineKind.Error); break; }
                    var host = _servers.ServerOf(found);
                    Out(found.Name.ToUpperInvariant(), TerminalLineKind.Heading);
                    Out("  host         " + (host?.Name ?? "\u2014"));
                    Out("  state        " + found.StateLabel,
                        found.IsRunning ? TerminalLineKind.Success : TerminalLineKind.Warning);
                    Out("  port         " + found.PortDisplay);
                    Out("  uptime       " + found.UptimeDisplay);
                    Out("  integration  " + (found.Integration == IntegrationState.Connected
                        ? "LIVE" : "NEXUS RECORD ONLY"));
                    break;

                case "security":
                    Out("SECURITY POSTURE", TerminalLineKind.Heading);
                    Out("  posture      " + _security.PostureLabel,
                        _security.PostureLabel == "NORMAL" ? TerminalLineKind.Success : TerminalLineKind.Warning);
                    Out("  " + _security.PostureDetail);
                    Out("  open alerts  " + _security.OpenAlerts);
                    Out("  critical 24h " + _security.CriticalEvents);
                    Out("  suspended    " + _security.SuspendedUsers);
                    Out("  sessions     " + _security.ActiveSessions);
                    break;

                case "alerts":
                    var open = _bus.Alerts.Where(a => !a.Acknowledged).ToList();
                    if (open.Count == 0) { Out("No open alerts.", TerminalLineKind.Success); break; }
                    Out("OPEN ALERTS  (" + open.Count + ")", TerminalLineKind.Heading);
                    foreach (var a in open)
                        Out("  [" + a.TimeDisplay + "] " + a.SeverityLabel.PadRight(10) + a.Title + " \u00B7 " + a.Detail,
                            TerminalLineKind.Warning);
                    break;

                case "logs":
                    var count = 12;
                    if (!string.IsNullOrWhiteSpace(arg)) int.TryParse(arg, out count);
                    if (count < 1) count = 12;
                    Out("AUDIT TRAIL  (last " + count + ")", TerminalLineKind.Heading);
                    foreach (var e in _bus.Events.Take(count))
                        Out("  [" + e.TimeDisplay + "] " + e.CategoryLabel.PadRight(11) + e.Message
                            + (string.IsNullOrEmpty(e.Detail) ? "" : " \u00B7 " + e.Detail));
                    break;

                case "tasks":
                    Out("TASK SUBSYSTEM", TerminalLineKind.Heading);
                    Out("  total        " + _tasks.Total);
                    Out("  completed    " + _tasks.Completed);
                    Out("  remaining    " + _tasks.Remaining);
                    Out("  in progress  " + _tasks.InProgress);
                    Out("  blocked      " + _tasks.Blocked);
                    Out("  overdue      " + _tasks.Overdue,
                        _tasks.Overdue > 0 ? TerminalLineKind.Warning : TerminalLineKind.Output);
                    Out("  completion   " + _tasks.CompletionPercent + "%");
                    break;

                case "automations":
                    Out("AUTOMATION RULES  (" + _automations.Rules.Count + ")", TerminalLineKind.Heading);
                    foreach (var r in _automations.Rules)
                        Out("  " + (r.IsEnabled ? "\u25CF " : "\u25CB ") + r.Name.PadRight(32)
                            + "WHEN " + r.TriggerLabel.PadRight(28) + "THEN " + r.ActionLabel
                            + "   fired " + r.TimesFired + "\u00D7",
                            r.IsEnabled ? TerminalLineKind.Output : TerminalLineKind.Warning);
                    break;

                case "ping":
                    if (string.IsNullOrWhiteSpace(arg)) { Out("Usage: ping <host or ip>", TerminalLineKind.Error); break; }
                    Out(_network.Ping(arg), TerminalLineKind.Success);
                    break;

                case "scan":
                    Out("Reading this machine's ARP cache. No probes are sent.", TerminalLineKind.Heading);
                    var newHosts = _network.DiscoverFromArpTable();
                    Out(newHosts == 0
                        ? "No previously unknown neighbours found."
                        : newHosts + " new host(s) registered in the inventory.",
                        TerminalLineKind.Success);
                    break;

                case "launch":
                    if (string.IsNullOrWhiteSpace(arg)) { Out("Usage: launch <section>", TerminalLineKind.Error); break; }
                    var key = arg.Trim().ToUpperInvariant();
                    NavigationRequested?.Invoke(key);
                    Out("Opening " + key + ".", TerminalLineKind.Success);
                    break;

                default:
                    Out("Unknown command \"" + cmd + "\". Type help for the command list.", TerminalLineKind.Error);
                    break;
            }

            _bus.Publish(EventCategory.Terminal, "Command executed", input.Trim(),
                         EventSeverity.Info, "TERMINAL");
            return lines;
        }

        /// <summary>Longest common prefix completion over the command list.</summary>
        public string Complete(string partial)
        {
            if (string.IsNullOrWhiteSpace(partial)) return partial;

            var matches = Commands.Where(c => c.StartsWith(partial.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0) return partial;
            if (matches.Count == 1) return matches[0] + " ";

            var prefix = new StringBuilder();
            for (var i = 0; i < matches[0].Length; i++)
            {
                var ch = matches[0][i];
                if (matches.Any(m => m.Length <= i || char.ToLowerInvariant(m[i]) != char.ToLowerInvariant(ch)))
                    break;
                prefix.Append(ch);
            }
            return prefix.ToString();
        }
    }
}

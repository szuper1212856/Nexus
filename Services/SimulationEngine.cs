using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using NEXUS.Models;

namespace NEXUS.Services
{
    /// <summary>
    /// Produces replies for simulated employees.
    ///
    /// This seam exists so a real language model can be added later without touching the
    /// service desk UI. The shipped implementation is deterministic and rule-based, and
    /// the UI labels it as such — nothing here is a language model.
    /// </summary>
    public interface IEmployeeResponder
    {
        string Id { get; }
        string DisplayName { get; }

        /// <summary>Opening report the employee files with the ticket.</summary>
        string OpeningReport(Employee employee, Ticket ticket, Workstation workstation);

        /// <summary>Reply to a technician message, given everything known about the situation.</summary>
        string Reply(Employee employee, Ticket ticket, Workstation workstation, string technicianMessage);

        /// <summary>What the employee says once the ticket is marked resolved.</summary>
        string Confirmation(Employee employee, Ticket ticket, Workstation workstation);
    }

    /// <summary>
    /// Rule-based responder. Replies are derived from the ticket category, the live state
    /// of the employee's simulated workstation, their account status, their temperament and
    /// what the technician actually asked — not from random selection.
    /// </summary>
    public class ScriptedEmployeeResponder : IEmployeeResponder
    {
        public string Id => "scripted";
        public string DisplayName => "Rule-based simulation";

        public string OpeningReport(Employee employee, Ticket ticket, Workstation workstation)
        {
            if (employee == null || ticket == null) return "";

            var detail = ticket.Category switch
            {
                TicketCategory.Hardware => Describe(workstation),
                TicketCategory.Account => employee.AccountState == AccountState.Locked
                    ? "It keeps telling me my account is locked out."
                    : "It won't accept my password any more.",
                TicketCategory.Network => "I can't reach anything on the network from my desk.",
                TicketCategory.Printing => "Nothing comes out of the printer and there's no error on screen.",
                TicketCategory.Software => "The application closes straight away when I open it.",
                TicketCategory.Access => "I get a permission error when I open that folder.",
                TicketCategory.Email => "My mail hasn't refreshed since this morning.",
                _ => "Something looks wrong and I'd rather someone checked it."
            };

            return Flavour(employee, ticket.Description + " " + detail);
        }

        public string Reply(Employee employee, Ticket ticket, Workstation workstation, string technicianMessage)
        {
            if (employee == null || ticket == null) return "";

            var q = (technicianMessage ?? "").ToLowerInvariant();

            // Account state overrides everything: a disabled account can't do anything.
            if (!employee.CanSignIn && (q.Contains("log in") || q.Contains("login") || q.Contains("sign in")))
                return Flavour(employee,
                    "I can't sign in at all. It says " + employee.AccountLabel.ToLowerInvariant() + " when I try.");

            if (Asks(q, "when", "start", "happen", "begin"))
                return Flavour(employee, "It started " + ticket.AgeDisplay.ToLowerInvariant()
                    + " ago. It was fine before that.");

            if (Asks(q, "what were you doing", "doing when", "before it"))
                return Flavour(employee, ticket.Category switch
                {
                    TicketCategory.Hardware => "I was copying files off the shared drive when it got bad.",
                    TicketCategory.Software => "I'd just opened the application like I always do.",
                    TicketCategory.Network => "I was on a call and it dropped out.",
                    TicketCategory.Printing => "I sent a document over and nothing came out.",
                    _ => "Nothing unusual, I was just working normally."
                });

            if (Asks(q, "restart", "reboot", "turn it off"))
                return workstation != null && workstation.IsOnline
                    ? Flavour(employee, "I restarted it about an hour ago and it made no difference.")
                    : Flavour(employee, "It won't come back on properly, so I can't restart it.");

            if (Asks(q, "error", "message", "says", "code"))
                return Flavour(employee, ticket.Category switch
                {
                    TicketCategory.Account => "The message is about the account being locked out.",
                    TicketCategory.Access => "It says I don't have permission to access this folder.",
                    TicketCategory.Software => "There's a box that says the application stopped working.",
                    _ => "There's no error, it just behaves badly."
                });

            if (Asks(q, "slow", "speed", "performance", "lag") && workstation != null)
                return Flavour(employee, "It's very slow, yes. Everything takes ages to open."
                    + (workstation.DiskUsed > 0.9 ? " It also keeps warning me about disk space." : ""));

            if (Asks(q, "disk", "space", "storage") && workstation != null)
                return Flavour(employee, "I get a warning about running out of space fairly often. "
                    + "I don't really know what's using it.");

            if (Asks(q, "printer", "print"))
                return Flavour(employee, "The printer is the one by the " + employee.DepartmentLabel.ToLowerInvariant()
                    + " desks. Other people seem to be having the same trouble.");

            if (Asks(q, "thank", "thanks", "cheers"))
                return Flavour(employee, "No problem, thanks for looking at it.");

            if (Asks(q, "fixed", "working now", "resolved", "better"))
                return ticket.IsResolved
                    ? Confirmation(employee, ticket, workstation)
                    : Flavour(employee, "Not yet, it's still doing the same thing.");

            if (Asks(q, "try", "can you", "could you", "please"))
                return Flavour(employee, "I can try that now. Give me a moment.");

            // Fall back to restating the live situation rather than inventing something.
            return Flavour(employee, "I'm not sure. " + Describe(workstation));
        }

        public string Confirmation(Employee employee, Ticket ticket, Workstation workstation)
            => Flavour(employee, "That's sorted it, thank you. It's behaving normally again.");

        private static bool Asks(string message, params string[] terms)
            => terms.Any(t => message.Contains(t));

        private static string Describe(Workstation workstation)
        {
            if (workstation == null) return "I don't have a machine assigned at the moment.";
            if (!workstation.IsOnline) return "The machine won't come on at all.";

            var notes = new List<string>();
            if (workstation.CpuLoad > 0.8) notes.Add("the fan is running constantly");
            if (workstation.MemoryLoad > 0.85) notes.Add("everything freezes when I have a few things open");
            if (workstation.DiskUsed > 0.9) notes.Add("it keeps warning me about disk space");

            return notes.Count == 0
                ? "It seems alright from where I'm sitting."
                : "Right now " + string.Join(", and ", notes) + ".";
        }

        /// <summary>Applies the employee's temperament to a factual reply.</summary>
        private static string Flavour(Employee employee, string body)
        {
            if (employee == null) return body;

            return employee.Temperament switch
            {
                Temperament.Terse => body,
                Temperament.Anxious => body + " Sorry to be a nuisance about it.",
                Temperament.Technical => body + " I did check the obvious things first.",
                Temperament.Frustrated => body + " It's been like this for a while now, honestly.",
                _ => "Hi \u2014 " + char.ToLowerInvariant(body[0]) + body.Substring(1)
            };
        }
    }

    /// <summary>
    /// Owns the fictional company: its employees, their workstations and the ticket queue.
    /// Every administrative action routes through here so it lands in the audit trail and
    /// stays consistent across the rest of NEXUS.
    ///
    /// Everything this engine touches is simulated. It never reaches a real machine.
    /// </summary>
    public class SimulationEngine
    {
        private readonly DataStore _store;
        private readonly EventBus _bus;
        private int _ticketSequence = 10480;

        public SimulationEngine(DataStore store, EventBus bus)
        {
            _store = store;
            _bus = bus;
            Responder = new ScriptedEmployeeResponder();
        }

        public IEmployeeResponder Responder { get; set; }

        public ObservableCollection<Employee> Employees => _store.Data.Employees;
        public ObservableCollection<Workstation> Workstations => _store.Data.Workstations;
        public ObservableCollection<Ticket> Tickets => _store.Data.Tickets;

        public event Action Changed;

        public const string CompanyName = "NEXUS CORPORATION";

        // ---------- queries ----------

        public int EmployeeCount => Employees.Count;
        public int OnlineEmployees => Employees.Count(e => e.IsOnline && e.CanSignIn);
        public int DisabledAccounts => Employees.Count(e => e.AccountState == AccountState.Disabled);
        public int LockedAccounts => Employees.Count(e => e.AccountState == AccountState.Locked);
        public int WorkstationCount => Workstations.Count;
        public int OnlineWorkstations => Workstations.Count(w => w.IsOnline);
        public int UnhealthyWorkstations => Workstations.Count(w => w.HealthLabel is "CRITICAL" or "DEGRADED");

        public int OpenTickets => Tickets.Count(t => t.IsOpen);
        public int UnassignedTickets => Tickets.Count(t => t.IsOpen && !t.IsAssigned);
        public int HighPriorityTickets => Tickets.Count(t => t.IsOpen && t.Priority == TicketPriority.High);
        public int CriticalTickets => Tickets.Count(t => t.IsOpen && t.Priority == TicketPriority.Critical);
        public int ResolvedTickets => Tickets.Count(t => t.IsResolved);

        public Employee FindEmployee(string term)
        {
            if (string.IsNullOrWhiteSpace(term)) return null;
            return Employees.FirstOrDefault(e => string.Equals(e.Username, term, StringComparison.OrdinalIgnoreCase))
                ?? Employees.FirstOrDefault(e => e.FullName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public Workstation WorkstationOf(Employee employee)
            => employee == null ? null
             : Workstations.FirstOrDefault(w =>
                   string.Equals(w.AssetTag, employee.WorkstationTag, StringComparison.OrdinalIgnoreCase));

        public Workstation WorkstationByTag(string tag)
            => string.IsNullOrWhiteSpace(tag) ? null
             : Workstations.FirstOrDefault(w => string.Equals(w.AssetTag, tag, StringComparison.OrdinalIgnoreCase));

        public Employee RequesterOf(Ticket ticket)
            => ticket == null ? null
             : Employees.FirstOrDefault(e =>
                   string.Equals(e.Username, ticket.RequesterUsername, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<Ticket> TicketsFor(Employee employee)
            => employee == null ? Enumerable.Empty<Ticket>()
             : Tickets.Where(t => string.Equals(t.RequesterUsername, employee.Username,
                                                StringComparison.OrdinalIgnoreCase));

        // ---------- ticket operations ----------

        public void Assign(Ticket ticket, string technician)
        {
            if (ticket == null) return;

            ticket.AssignedTo = technician ?? "";
            ticket.Status = ticket.Status == TicketStatus.Open ? TicketStatus.InProgress : ticket.Status;
            Stamp(ticket);

            _bus.Publish(EventCategory.Ticket, "TICKET_ASSIGNED",
                ticket.Reference + " \u00B7 " + ticket.Title + " \u2192 " + ticket.AssignedLabel,
                EventSeverity.Info, "SERVICE DESK");
            Touch();
        }

        public void SetStatus(Ticket ticket, TicketStatus status)
        {
            if (ticket == null || ticket.Status == status) return;

            var previous = ticket.StatusLabel;
            ticket.Status = status;
            Stamp(ticket);

            if (status == TicketStatus.Resolved)
            {
                ticket.ResolvedUtc = DateTime.UtcNow;

                var employee = RequesterOf(ticket);
                var workstation = WorkstationByTag(ticket.WorkstationTag);
                var confirmation = Responder.Confirmation(employee, ticket, workstation);

                if (employee != null && !string.IsNullOrEmpty(confirmation))
                    ticket.Conversation.Add(new TicketMessage
                    {
                        Author = employee.FullName,
                        Body = confirmation,
                        FromTechnician = false
                    });
            }

            _bus.Publish(EventCategory.Ticket, "TICKET_UPDATED",
                ticket.Reference + " \u00B7 " + previous + " \u2192 " + ticket.StatusLabel,
                status == TicketStatus.Resolved ? EventSeverity.Notice : EventSeverity.Info, "SERVICE DESK");
            Touch();
        }

        public void SetPriority(Ticket ticket, TicketPriority priority)
        {
            if (ticket == null || ticket.Priority == priority) return;

            var previous = ticket.PriorityLabel;
            ticket.Priority = priority;
            Stamp(ticket);

            _bus.Publish(EventCategory.Ticket, "TICKET_UPDATED",
                ticket.Reference + " \u00B7 priority " + previous + " \u2192 " + ticket.PriorityLabel,
                EventSeverity.Info, "SERVICE DESK");
            Touch();
        }

        /// <summary>Posts a technician message and lets the requester respond in kind.</summary>
        public void SendMessage(Ticket ticket, string body, string technician)
        {
            if (ticket == null || string.IsNullOrWhiteSpace(body)) return;

            ticket.Conversation.Add(new TicketMessage
            {
                Author = string.IsNullOrWhiteSpace(technician) ? "Technician" : technician,
                Body = body.Trim(),
                FromTechnician = true
            });

            var employee = RequesterOf(ticket);
            var workstation = WorkstationByTag(ticket.WorkstationTag);

            if (employee != null && ticket.IsOpen)
            {
                var reply = Responder.Reply(employee, ticket, workstation, body);
                if (!string.IsNullOrEmpty(reply))
                    ticket.Conversation.Add(new TicketMessage
                    {
                        Author = employee.FullName,
                        Body = reply,
                        FromTechnician = false
                    });

                employee.LastActivityUtc = DateTime.UtcNow;
            }

            Stamp(ticket);
            _bus.Publish(EventCategory.Ticket, "TICKET_UPDATED",
                ticket.Reference + " \u00B7 conversation updated", EventSeverity.Info, "SERVICE DESK");
            Touch();
        }

        public Ticket CreateTicket(Employee requester, string title, string description,
                                   TicketCategory category, TicketPriority priority,
                                   TicketDifficulty difficulty = TicketDifficulty.Easy, string rootCause = "")
        {
            if (requester == null) return null;

            var ticket = new Ticket
            {
                Reference = "INC-" + (++_ticketSequence),
                Title = title,
                Description = description,
                RequesterUsername = requester.Username,
                Department = requester.Department,
                WorkstationTag = requester.WorkstationTag,
                Category = category,
                Priority = priority,
                Difficulty = difficulty,
                RootCause = rootCause
            };

            var workstation = WorkstationOf(requester);
            var opening = Responder.OpeningReport(requester, ticket, workstation);
            if (!string.IsNullOrEmpty(opening))
                ticket.Conversation.Add(new TicketMessage
                {
                    Author = requester.FullName,
                    Body = opening,
                    FromTechnician = false
                });

            Tickets.Insert(0, ticket);

            _bus.Publish(EventCategory.Ticket, "TICKET_CREATED",
                ticket.Reference + " \u00B7 " + ticket.Title + " \u00B7 " + requester.FullName,
                ticket.Priority >= TicketPriority.High ? EventSeverity.Warning : EventSeverity.Notice,
                "SERVICE DESK");

            Touch();
            return ticket;
        }

        // ---------- account administration ----------

        public void SetAccountState(Employee employee, AccountState state, string actor)
        {
            if (employee == null || employee.AccountState == state) return;

            var previous = employee.AccountLabel;
            employee.AccountState = state;

            if (state != AccountState.Active)
            {
                employee.IsOnline = false;

                // A signed-out employee's workstation stops being reachable to them.
                var workstation = WorkstationOf(employee);
                if (workstation != null && state == AccountState.Disabled) workstation.IsOnline = false;
            }
            else
            {
                employee.FailedSignIns = 0;
            }

            var code = state switch
            {
                AccountState.Disabled => "ACCOUNT_DISABLED",
                AccountState.Locked => "ACCOUNT_LOCKED",
                AccountState.PasswordExpired => "PASSWORD_EXPIRED",
                _ => "ACCOUNT_ENABLED"
            };

            _bus.Publish(EventCategory.Simulation, code,
                employee.FullName + " \u00B7 " + previous + " \u2192 " + employee.AccountLabel
                + " \u00B7 by " + actor,
                state == AccountState.Active ? EventSeverity.Notice : EventSeverity.Critical, "DIRECTORY");
            Touch();
        }

        public void Unlock(Employee employee, string actor)
        {
            if (employee == null || employee.AccountState != AccountState.Locked) return;

            employee.AccountState = AccountState.Active;
            employee.FailedSignIns = 0;

            _bus.Publish(EventCategory.Simulation, "ACCOUNT_UNLOCKED",
                employee.FullName + " \u00B7 unlocked by " + actor, EventSeverity.Notice, "DIRECTORY");
            Touch();
        }

        /// <summary>
        /// Resets the simulated password lifecycle. No credential is generated or stored —
        /// the simulation only tracks the state of the account.
        /// </summary>
        public void ResetPassword(Employee employee, string actor)
        {
            if (employee == null) return;

            employee.PasswordSetUtc = DateTime.UtcNow;
            employee.FailedSignIns = 0;
            if (employee.AccountState == AccountState.PasswordExpired || employee.AccountState == AccountState.Locked)
                employee.AccountState = AccountState.Active;
            employee.RefreshTimeDependent();

            _bus.Publish(EventCategory.Simulation, "PASSWORD_RESET",
                employee.FullName + " \u00B7 reset by " + actor
                + " \u00B7 account marked as requiring a new password at next sign-in",
                EventSeverity.Warning, "DIRECTORY");
            Touch();
        }

        public void SetDepartment(Employee employee, Department department, string actor)
        {
            if (employee == null || employee.Department == department) return;

            var previous = employee.DepartmentLabel;
            employee.Department = department;

            _bus.Publish(EventCategory.Simulation, "USER_UPDATED",
                employee.FullName + " \u00B7 " + previous + " \u2192 " + employee.DepartmentLabel
                + " \u00B7 by " + actor, EventSeverity.Warning, "DIRECTORY");
            Touch();
        }

        public void SetMfa(Employee employee, bool enabled, string actor)
        {
            if (employee == null || employee.MfaEnabled == enabled) return;

            employee.MfaEnabled = enabled;
            _bus.Publish(EventCategory.Simulation, enabled ? "MFA_ENROLLED" : "MFA_RESET",
                employee.FullName + " \u00B7 by " + actor,
                enabled ? EventSeverity.Notice : EventSeverity.Warning, "DIRECTORY");
            Touch();
        }

        // ---------- upkeep ----------

        private void Stamp(Ticket ticket)
        {
            ticket.UpdatedUtc = DateTime.UtcNow;
            ticket.RefreshTimeDependent();
        }

        public void RefreshTimeDependent()
        {
            foreach (var t in Tickets) t.RefreshTimeDependent();
            foreach (var e in Employees) e.RefreshTimeDependent();
            foreach (var w in Workstations) w.Refresh();
        }

        public void Touch()
        {
            Changed?.Invoke();
            _store.RequestSave();
        }

        // ---------- provisioning ----------

        /// <summary>
        /// Builds the company once. State is persisted, so the same people, machines and
        /// history are there on every launch.
        /// </summary>
        public void SeedCompany()
        {
            var roster = new[]
            {
                ("Maria","Garcia",Department.Marketing,"Senior Marketing Specialist",Temperament.Patient),
                ("James","Whitfield",Department.Engineering,"Senior Software Engineer",Temperament.Technical),
                ("Aisha","Okafor",Department.Finance,"Financial Controller",Temperament.Terse),
                ("Tomas","Lindqvist",Department.IT,"Infrastructure Engineer",Temperament.Technical),
                ("Priya","Raman",Department.HR,"HR Business Partner",Temperament.Patient),
                ("Daniel","Brennan",Department.Sales,"Account Executive",Temperament.Frustrated),
                ("Sofia","Almeida",Department.Operations,"Operations Coordinator",Temperament.Anxious),
                ("Marcus","Webb",Department.Management,"Head of Operations",Temperament.Terse),
                ("Lena","Fischer",Department.Engineering,"QA Engineer",Temperament.Technical),
                ("Omar","Haddad",Department.IT,"Service Desk Analyst",Temperament.Patient),
                ("Grace","Mbeki",Department.Finance,"Accounts Payable Clerk",Temperament.Anxious),
                ("Victor","Novak",Department.Sales,"Regional Sales Manager",Temperament.Frustrated),
                ("Hannah","Ellis",Department.Marketing,"Content Designer",Temperament.Patient),
                ("Ravi","Chandra",Department.Engineering,"Platform Engineer",Temperament.Technical),
                ("Elena","Rossi",Department.HR,"Recruitment Lead",Temperament.Patient),
                ("Peter","Nyland",Department.Operations,"Logistics Planner",Temperament.Terse),
                ("Chloe","Bennett",Department.Marketing,"Campaign Analyst",Temperament.Anxious),
                ("Andre","Silva",Department.IT,"Systems Administrator",Temperament.Technical),
                ("Nora","Haugen",Department.Finance,"Payroll Specialist",Temperament.Patient),
                ("Isaac","Turner",Department.Sales,"Sales Development Rep",Temperament.Frustrated),
                ("Yuki","Tanaka",Department.Engineering,"Frontend Engineer",Temperament.Technical),
                ("Fatima","Zahra",Department.Operations,"Facilities Manager",Temperament.Patient),
                ("Robert","Kane",Department.Management,"Chief Technology Officer",Temperament.Terse),
                ("Julia","Moreau",Department.HR,"People Operations Assistant",Temperament.Anxious)
            };

            var apps = new Dictionary<Department, string[]>
            {
                { Department.Marketing, new[]{ "Microsoft 365", "Adobe Photoshop", "Teams", "Chrome" } },
                { Department.Engineering, new[]{ "Visual Studio", "Docker Desktop", "Teams", "Chrome", "Git" } },
                { Department.Finance, new[]{ "Microsoft 365", "SAP Client", "Teams", "Excel Add-ins" } },
                { Department.HR, new[]{ "Microsoft 365", "Workday", "Teams" } },
                { Department.Sales, new[]{ "Microsoft 365", "Salesforce", "Teams", "Zoom" } },
                { Department.IT, new[]{ "Microsoft 365", "PowerShell", "Remote Desktop", "Teams", "Wireshark" } },
                { Department.Operations, new[]{ "Microsoft 365", "Teams", "Tableau" } },
                { Department.Management, new[]{ "Microsoft 365", "Teams", "Power BI" } }
            };

            var prefix = new Dictionary<Department, string>
            {
                { Department.Marketing, "MKT" }, { Department.Engineering, "ENG" },
                { Department.Finance, "FIN" }, { Department.HR, "HRD" },
                { Department.Sales, "SLS" }, { Department.IT, "ITD" },
                { Department.Operations, "OPS" }, { Department.Management, "MGT" }
            };

            var counters = new Dictionary<Department, int>();
            var host = 20;

            foreach (var (first, last, dept, title, temperament) in roster)
            {
                counters.TryGetValue(dept, out var n);
                counters[dept] = ++n;

                var tag = prefix[dept] + "-PC" + n.ToString("00");
                var username = (first.Substring(0, 1) + last).ToLowerInvariant();

                var employee = new Employee
                {
                    FirstName = first,
                    LastName = last,
                    Username = username,
                    Department = dept,
                    JobTitle = title,
                    Temperament = temperament,
                    WorkstationTag = tag,
                    HiredUtc = DateTime.UtcNow.AddDays(-120 - n * 17),
                    LastActivityUtc = DateTime.UtcNow.AddMinutes(-(n * 7 % 90)),
                    IsOnline = n % 5 != 0
                };

                foreach (var app in apps[dept]) employee.Applications.Add(app);
                employee.Groups.Add(dept.ToString() + "-Users");
                employee.Groups.Add("All-Staff");
                if (dept == Department.IT) employee.Groups.Add("IT-Admins");
                if (dept == Department.Management) employee.Groups.Add("Managers");

                Employees.Add(employee);

                Workstations.Add(new Workstation
                {
                    AssetTag = tag,
                    Model = dept == Department.Engineering ? "Precision 5680" : "OptiPlex 7010",
                    OperatingSystem = "Windows 11 Pro",
                    IpAddress = "10.20." + ((int)dept + 1) + "." + (host++),
                    AssignedUsername = username,
                    Department = dept,
                    TotalMemoryGb = dept == Department.Engineering ? 32 : 16,
                    TotalDiskGb = dept == Department.Engineering ? 1024 : 512,
                    LastBootUtc = DateTime.UtcNow.AddHours(-(4 + n % 20)),
                    CpuLoad = 0.12 + (n % 5) * 0.06,
                    MemoryLoad = 0.34 + (n % 4) * 0.08,
                    DiskUsed = 0.40 + (n % 6) * 0.05,
                    IsOnline = employee.IsOnline
                });
            }

            SeedOpeningTickets();

            _bus.Publish(EventCategory.Simulation, "COMPANY_PROVISIONED",
                CompanyName + " \u00B7 " + Employees.Count + " employees \u00B7 "
                + Workstations.Count + " workstations \u00B7 " + Tickets.Count + " open tickets",
                EventSeverity.Notice, "SIMULATION");
            Touch();
        }

        /// <summary>The queue an operator finds waiting on their first shift.</summary>
        private void SeedOpeningTickets()
        {
            void Raise(string username, string title, string description, TicketCategory category,
                       TicketPriority priority, TicketDifficulty difficulty, string rootCause,
                       Action<Employee, Workstation> arrange = null)
            {
                var employee = FindEmployee(username);
                if (employee == null) return;

                var workstation = WorkstationOf(employee);
                arrange?.Invoke(employee, workstation);

                var ticket = CreateTicket(employee, title, description, category, priority, difficulty, rootCause);
                if (ticket != null) ticket.CreatedUtc = DateTime.UtcNow.AddMinutes(-(15 + Tickets.Count * 23));
            }

            Raise("mgarcia", "Computer extremely slow",
                "My computer has become very slow since this morning.",
                TicketCategory.Hardware, TicketPriority.High, TicketDifficulty.Medium,
                "The system disk is nearly full, which is causing heavy paging.",
                (e, w) => { if (w != null) { w.DiskUsed = 0.97; w.MemoryLoad = 0.92; w.CpuLoad = 0.87; } });

            Raise("gmbeki", "Cannot sign in to my account",
                "I have been locked out after trying my password a few times.",
                TicketCategory.Account, TicketPriority.Normal, TicketDifficulty.Easy,
                "Account lockout threshold reached.",
                (e, w) => { e.AccountState = AccountState.Locked; e.FailedSignIns = 5; e.IsOnline = false; });

            Raise("dbrennan", "Cannot access the Finance shared folder",
                "I need a document from the Finance share but I get a permission error.",
                TicketCategory.Access, TicketPriority.Normal, TicketDifficulty.Medium,
                "Requester is not a member of the Finance-Users group, which is correct policy.");

            Raise("salmeida", "Printer on the Operations floor not responding",
                "Nothing prints and there is no error shown on my screen.",
                TicketCategory.Printing, TicketPriority.Normal, TicketDifficulty.Medium,
                "Print spooler on the shared queue has stalled.");

            Raise("jwhitfield", "Docker Desktop will not start",
                "The application exits immediately when I launch it.",
                TicketCategory.Software, TicketPriority.Normal, TicketDifficulty.Hard,
                "Virtualisation support was disabled by a recent firmware update.");

            Raise("ctanaka", "Mailbox has stopped receiving messages", "", TicketCategory.Email,
                TicketPriority.Low, TicketDifficulty.Easy, "");

            Raise("vnovak", "VPN disconnects every few minutes",
                "I keep dropping off the VPN while working from home.",
                TicketCategory.Network, TicketPriority.High, TicketDifficulty.Hard,
                "Intermittent packet loss on the requester's home uplink.");

            Raise("nhaugen", "Payroll application reports a licence error",
                "A licence message appears when I open the payroll system.",
                TicketCategory.Software, TicketPriority.High, TicketDifficulty.Medium,
                "Licence server allocation expired for this user.");
        }
    }
}

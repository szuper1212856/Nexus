using System;
using System.Collections.ObjectModel;
using System.Linq;
using NEXUS.Models;

namespace NEXUS.Services
{
    /// <summary>
    /// Manages server records and the services running on them. The record marked
    /// IsLocalMachine is fed with genuine telemetry from SystemInfoService; every other
    /// server is a NEXUS-side model until a real integration is added.
    /// </summary>
    public class ServerManager
    {
        private readonly DataStore _store;
        private readonly EventBus _bus;
        private readonly SystemInfoService _sysinfo;

        public ServerManager(DataStore store, EventBus bus, SystemInfoService sysinfo)
        {
            _store = store;
            _bus = bus;
            _sysinfo = sysinfo;
        }

        public ObservableCollection<ManagedServer> Servers => _store.Data.Servers;

        public event Action Changed;

        public int OnlineCount => Servers.Count(s => s.IsOnline);
        public int RunningServices => Servers.Sum(s => s.RunningServices);
        public int TotalServices => Servers.Sum(s => s.Services.Count);

        public ManagedServer Find(string name)
            => Servers.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? Servers.FirstOrDefault(s => s.Name != null &&
                       s.Name.IndexOf(name ?? "", StringComparison.OrdinalIgnoreCase) >= 0);

        public ManagedService FindService(string name)
            => Servers.SelectMany(s => s.Services)
                      .FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? Servers.SelectMany(s => s.Services)
                         .FirstOrDefault(x => x.Name != null &&
                             x.Name.IndexOf(name ?? "", StringComparison.OrdinalIgnoreCase) >= 0);

        public ManagedServer ServerOf(ManagedService service)
            => Servers.FirstOrDefault(s => s.Services.Contains(service));

        // ---------- service control ----------

        public void StartService(ManagedService service)
        {
            if (service == null || service.IsRunning) return;

            service.State = ServiceState.Running;
            service.StartedUtc = DateTime.UtcNow;

            _bus.Publish(EventCategory.Service, "Service started",
                service.Name + Describe(service), EventSeverity.Notice, "SERVICES");
            Touch();
        }

        public void StopService(ManagedService service)
        {
            if (service == null || service.State == ServiceState.Stopped) return;

            service.State = ServiceState.Stopped;
            service.StartedUtc = null;

            _bus.Publish(EventCategory.Service, "Service stopped",
                service.Name + Describe(service), EventSeverity.Warning, "SERVICES");
            _bus.Signal(TriggerType.ServiceStopped, service.Name, "Stopped by operator", service);
            Touch();
        }

        public void RestartService(ManagedService service)
        {
            if (service == null) return;

            service.State = ServiceState.Running;
            service.StartedUtc = DateTime.UtcNow;

            _bus.Publish(EventCategory.Service, "Service restarted",
                service.Name + Describe(service), EventSeverity.Notice, "SERVICES");
            Touch();
        }

        /// <summary>Honest label so a modelled service is never mistaken for a live one.</summary>
        private static string Describe(ManagedService service)
            => service.Integration == IntegrationState.Connected
                ? ""
                : " \u00B7 NEXUS record only, no external service was contacted";

        public void SetServerStatus(ManagedServer server, DeviceStatus status)
        {
            if (server == null || server.Status == status) return;

            var previous = server.StatusLabel;
            server.Status = status;

            if (status == DeviceStatus.Offline)
            {
                foreach (var s in server.Services)
                {
                    s.State = ServiceState.Stopped;
                    s.StartedUtc = null;
                }
                _bus.Signal(TriggerType.ServerOffline, server.Name, "Marked offline", server);
            }
            else if (status == DeviceStatus.Online && !server.BootedUtc.HasValue)
            {
                server.BootedUtc = DateTime.UtcNow;
            }

            server.Refresh();
            _bus.Publish(EventCategory.Server, "Server status changed",
                server.Name + " \u00B7 " + previous + " \u2192 " + server.StatusLabel,
                status == DeviceStatus.Offline ? EventSeverity.Critical : EventSeverity.Notice, "SERVERS");
            Touch();
        }

        public void AddService(ManagedServer server, ManagedService service)
        {
            if (server == null || service == null) return;

            server.Services.Add(service);
            server.Refresh();
            _bus.Publish(EventCategory.Service, "Service registered",
                service.Name + " on " + server.Name, EventSeverity.Notice, "SERVICES");
            Touch();
        }

        public void RemoveService(ManagedServer server, ManagedService service)
        {
            if (server == null || service == null) return;

            server.Services.Remove(service);
            server.Refresh();
            _bus.Publish(EventCategory.Service, "Service removed",
                service.Name + " from " + server.Name, EventSeverity.Warning, "SERVICES");
            Touch();
        }

        /// <summary>Pushes live telemetry into the local-machine record and refreshes the rest.</summary>
        public void RefreshMetrics()
        {
            foreach (var server in Servers)
            {
                if (server.IsLocalMachine)
                {
                    server.CpuLoad = _sysinfo.CpuLoad;
                    server.MemoryLoad = _sysinfo.MemoryLoad;
                    server.StorageLoad = _sysinfo.StorageLoad;
                    server.BootedUtc = DateTime.UtcNow - _sysinfo.Uptime;
                }

                foreach (var s in server.Services) s.RefreshTimeDependent();
                server.Refresh();
            }

            Changed?.Invoke();
        }

        public void Touch()
        {
            Changed?.Invoke();
            _store.RequestSave();
        }

        public void SeedDefaults()
        {
            var local = new ManagedServer
            {
                Name = Environment.MachineName,
                Host = "127.0.0.1",
                OperatingSystem = _sysinfo.OperatingSystem,
                IsLocalMachine = true,
                Status = DeviceStatus.Online,
                Integration = IntegrationState.Connected,
                IntegrationId = "local",
                BootedUtc = DateTime.UtcNow - _sysinfo.Uptime
            };
            local.Services.Add(new ManagedService
            {
                Name = "NEXUS Core",
                Description = "Command centre runtime",
                State = ServiceState.Running,
                StartedUtc = DateTime.UtcNow,
                Integration = IntegrationState.Connected,
                IntegrationId = "local"
            });
            Servers.Add(local);

            var home = new ManagedServer
            {
                Name = "Home Server",
                Host = "192.168.1.20",
                OperatingSystem = "Windows",
                Status = DeviceStatus.Online,
                BootedUtc = DateTime.UtcNow.AddDays(-3).AddHours(-7),
                CpuLoad = 0.18,
                MemoryLoad = 0.46,
                StorageLoad = 0.62
            };
            home.Services.Add(new ManagedService
            {
                Name = "Minecraft",
                Description = "Java edition game server",
                Port = 25565,
                State = ServiceState.Running,
                StartedUtc = DateTime.UtcNow.AddHours(-9)
            });
            home.Services.Add(new ManagedService
            {
                Name = "Jellyfin",
                Description = "Media streaming service",
                Port = 8096,
                State = ServiceState.Running,
                StartedUtc = DateTime.UtcNow.AddDays(-3)
            });
            home.Services.Add(new ManagedService
            {
                Name = "File Share",
                Description = "SMB share",
                Port = 445,
                State = ServiceState.Stopped
            });
            Servers.Add(home);

            _bus.Publish(EventCategory.System, "Server inventory provisioned",
                Servers.Count + " servers \u00B7 " + TotalServices + " services",
                EventSeverity.Notice, "SERVERS");
            Touch();
        }
    }
}

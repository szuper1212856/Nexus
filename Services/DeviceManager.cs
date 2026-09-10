using System;
using System.Collections.ObjectModel;
using System.Linq;
using NEXUS.Models;
using NEXUS.Services.Integrations;

namespace NEXUS.Services
{
    /// <summary>Owns the device inventory and routes controls to the right integration backend.</summary>
    public class DeviceManager
    {
        private readonly DataStore _store;
        private readonly EventBus _bus;
        private readonly IntegrationManager _integrations;

        public DeviceManager(DataStore store, EventBus bus, IntegrationManager integrations)
        {
            _store = store;
            _bus = bus;
            _integrations = integrations;
        }

        public ObservableCollection<Device> Devices => _store.Data.Devices;
        public IntegrationManager Integrations => _integrations;

        public event Action Changed;

        public int OnlineCount => Devices.Count(d => d.Status == DeviceStatus.Online);
        public int OfflineCount => Devices.Count(d => d.Status == DeviceStatus.Offline);
        public int Total => Devices.Count;

        public Device Find(string name)
            => Devices.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? Devices.FirstOrDefault(d => d.Name != null &&
                       d.Name.IndexOf(name ?? "", StringComparison.OrdinalIgnoreCase) >= 0);

        public void Add(Device device)
        {
            if (device == null) return;
            Devices.Add(device);
            _bus.Publish(EventCategory.Device, "Device registered", device.Name + " \u00B7 " + device.TypeLabel,
                         EventSeverity.Notice, "DEVICES");
            Touch();
        }

        public void Remove(Device device)
        {
            if (device == null || !Devices.Contains(device)) return;
            Devices.Remove(device);
            _bus.Publish(EventCategory.Device, "Device removed", device.Name, EventSeverity.Notice, "DEVICES");
            Touch();
        }

        /// <summary>Runs a control through the device's backend and records exactly what happened.</summary>
        public IntegrationResult Execute(Device device, string commandKey)
        {
            if (device == null) return IntegrationResult.Refused("No device selected.");

            var backend = _integrations.Resolve(device);

            if (!backend.SupportsCommand(device, commandKey))
            {
                var refusal = IntegrationResult.Refused(
                    "This integration does not currently expose the \"" + commandKey + "\" control.");

                device.LastCommand = commandKey;
                device.LastResponse = refusal.Message;
                device.LastResponseUtc = DateTime.UtcNow;
                device.LastCommandSucceeded = false;

                _bus.Publish(EventCategory.Device, "Control unavailable on " + device.Name,
                    refusal.Message, EventSeverity.Warning, "DEVICES");
                Touch();
                return refusal;
            }

            var result = backend.Execute(device, commandKey);

            device.LastCommand = commandKey;
            device.LastResponse = result.Message;
            device.LastResponseUtc = DateTime.UtcNow;
            device.LastCommandSucceeded = result.Handled;

            if (result.Handled)
            {
                device.LastSeenUtc = DateTime.UtcNow;
                _bus.Publish(EventCategory.Device,
                    device.Name + " \u00B7 " + commandKey.ToUpperInvariant(),
                    result.Message, EventSeverity.Info, "DEVICES");
            }
            else
            {
                _bus.Publish(EventCategory.Device,
                    "Command refused on " + device.Name,
                    result.Message, EventSeverity.Warning, "DEVICES");
            }

            Touch();
            return result;
        }

        public void SetStatus(Device device, DeviceStatus status)
        {
            if (device == null || device.Status == status) return;

            var previous = device.StatusLabel;
            device.Status = status;
            if (status == DeviceStatus.Online) device.LastSeenUtc = DateTime.UtcNow;

            _bus.Publish(EventCategory.Device, "Device status changed",
                device.Name + " \u00B7 " + previous + " \u2192 " + device.StatusLabel,
                status == DeviceStatus.Offline ? EventSeverity.Warning : EventSeverity.Info, "DEVICES");

            _bus.Signal(status == DeviceStatus.Online ? TriggerType.DeviceOnline : TriggerType.DeviceOffline,
                        device.Name, device.TypeLabel, device);

            Touch();
        }

        /// <summary>True when the device's backend can perform this control.</summary>
        public bool Supports(Device device, string commandKey)
            => device != null && _integrations.Resolve(device).SupportsCommand(device, commandKey);

        /// <summary>Re-evaluates the inventory. Real reachability checks belong to the network manager.</summary>
        public void RunHealthCheck(bool quiet = false)
        {
            foreach (var d in Devices) d.RefreshTimeDependent();

            if (!quiet)
                _bus.Publish(EventCategory.System, "Device sweep complete",
                    OnlineCount + " online \u00B7 " + OfflineCount + " offline \u00B7 " + Total + " registered",
                    EventSeverity.Info, "DEVICES");

            Touch();
        }

        public void Touch()
        {
            Changed?.Invoke();
            _store.RequestSave();
        }

        /// <summary>Inventory written on first run so the console has something to operate on.</summary>
        public void SeedDefaults()
        {
            void Add(string name, DeviceType type, LinkKind link, string ip, string vendor,
                     DeviceStatus status, string parent, string integrationId = "simulated")
            {
                Devices.Add(new Device
                {
                    Name = name,
                    Type = type,
                    Link = link,
                    IpAddress = ip,
                    Vendor = vendor,
                    Status = status,
                    ParentName = parent,
                    IntegrationId = integrationId,
                    Integration = integrationId == "simulated" ? IntegrationState.Simulated : IntegrationState.Unlinked,
                    LastSeenUtc = status == DeviceStatus.Online ? DateTime.UtcNow : DateTime.UtcNow.AddHours(-6)
                });
            }

            Add("Router", DeviceType.Router, LinkKind.Ethernet, "192.168.1.1", "Gateway", DeviceStatus.Online, "");
            Add("Main PC", DeviceType.Workstation, LinkKind.Ethernet, "192.168.1.10", "Custom Build", DeviceStatus.Online, "Router");
            Add("Home Server", DeviceType.Server, LinkKind.Ethernet, "192.168.1.20", "Local", DeviceStatus.Online, "Router");
            Add("Samsung Smart TV", DeviceType.Television, LinkKind.WiFi, "192.168.1.32", "Samsung", DeviceStatus.Online, "Router", "samsung-tv");
            Add("Laptop", DeviceType.Laptop, LinkKind.WiFi, "192.168.1.41", "Portable", DeviceStatus.Online, "Router");
            Add("Phone", DeviceType.Phone, LinkKind.WiFi, "192.168.1.55", "Mobile", DeviceStatus.Online, "Main PC");
            Add("Printer", DeviceType.Printer, LinkKind.WiFi, "192.168.1.60", "Office", DeviceStatus.Offline, "Router");

            _bus.Publish(EventCategory.System, "Device inventory provisioned",
                Devices.Count + " devices registered", EventSeverity.Notice, "DEVICES");
            Touch();
        }
    }
}

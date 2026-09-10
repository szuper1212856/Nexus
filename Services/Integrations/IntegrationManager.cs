using System;
using System.Collections.Generic;
using NEXUS.Models;

namespace NEXUS.Services.Integrations
{
    public class IntegrationResult
    {
        public bool Handled { get; set; }
        public string Message { get; set; } = "";
        public EventSeverity Severity { get; set; } = EventSeverity.Info;

        public static IntegrationResult Ok(string message)
            => new IntegrationResult { Handled = true, Message = message };

        public static IntegrationResult Refused(string message)
            => new IntegrationResult { Handled = false, Message = message, Severity = EventSeverity.Warning };
    }

    /// <summary>
    /// Contract every device backend implements. Adding real hardware support later means
    /// dropping in one class here and registering it, with no changes to the UI or managers.
    /// </summary>
    public interface IDeviceIntegration
    {
        string Id { get; }
        string DisplayName { get; }

        /// <summary>Device types this backend can drive.</summary>
        bool Supports(DeviceType type);

        /// <summary>What NEXUS may honestly claim about devices bound to this backend.</summary>
        IntegrationState State { get; }

        /// <summary>Executes a control. Implementations must not report success they cannot verify.</summary>
        IntegrationResult Execute(Device device, string commandKey);

        /// <summary>
        /// Whether this backend can genuinely perform a given control. The device console
        /// greys out anything that returns false and says so, rather than offering a
        /// button that would silently do nothing.
        /// </summary>
        bool SupportsCommand(Device device, string commandKey);
    }

    /// <summary>
    /// Default backend. Applies commands to the NEXUS record only, and says so.
    /// This is what keeps the console fully interactive without ever pretending
    /// that a physical device was contacted.
    /// </summary>
    public class SimulatedIntegration : IDeviceIntegration
    {
        public string Id => "simulated";
        public string DisplayName => "NEXUS Simulation";
        public IntegrationState State => IntegrationState.Simulated;
        public bool Supports(DeviceType type) => true;

        private static readonly string[] Handled =
        {
            "power", "vol-up", "vol-down", "mute", "input", "home", "back",
            "lock", "restart", "ping", "clients", "logs"
        };

        public bool SupportsCommand(Device device, string commandKey)
            => Array.IndexOf(Handled, commandKey) >= 0;

        public IntegrationResult Execute(Device device, string commandKey)
        {
            if (device == null) return IntegrationResult.Refused("No device supplied.");

            switch (commandKey)
            {
                case "power":
                    device.IsPowered = !device.IsPowered;
                    device.Status = device.IsPowered ? DeviceStatus.Online : DeviceStatus.Offline;
                    return IntegrationResult.Ok((device.IsPowered ? "Powered on" : "Put into standby")
                                                + " (simulated \u2014 record only)");

                case "vol-up":
                    device.Volume = device.Volume + 5;
                    device.IsMuted = false;
                    return IntegrationResult.Ok("Volume " + device.Volume + " (simulated)");

                case "vol-down":
                    device.Volume = device.Volume - 5;
                    device.IsMuted = false;
                    return IntegrationResult.Ok("Volume " + device.Volume + " (simulated)");

                case "mute":
                    device.IsMuted = !device.IsMuted;
                    return IntegrationResult.Ok((device.IsMuted ? "Muted" : "Unmuted") + " (simulated)");

                case "input":
                    device.CurrentInput = NextInput(device.CurrentInput);
                    return IntegrationResult.Ok("Input set to " + device.CurrentInput + " (simulated)");

                case "home":
                    return IntegrationResult.Ok("Home screen requested (simulated)");

                case "back":
                    return IntegrationResult.Ok("Back navigation sent (simulated)");

                case "lock":
                    return IntegrationResult.Ok("Lock requested (simulated)");

                case "restart":
                    device.Status = DeviceStatus.Degraded;
                    return IntegrationResult.Ok("Restart sequence started (simulated)");

                case "ping":
                    device.LastSeenUtc = DateTime.UtcNow;
                    return IntegrationResult.Ok("Reachability refreshed from the NEXUS record (simulated)");

                case "clients":
                    return IntegrationResult.Ok("Client list requested (simulated)");

                case "logs":
                    return IntegrationResult.Ok("Log tail requested (simulated)");

                default:
                    return IntegrationResult.Refused("Command \"" + commandKey + "\" is not implemented.");
            }
        }

        private static string NextInput(string current) => current switch
        {
            "HDMI 1" => "HDMI 2",
            "HDMI 2" => "HDMI 3",
            "HDMI 3" => "TV",
            _ => "HDMI 1"
        };
    }

    /// <summary>
    /// Placeholder for the real Samsung integration. It deliberately refuses every
    /// command until pairing is implemented, rather than faking success. Wiring this up
    /// later means implementing Execute against the TV's local API and returning
    /// IntegrationState.Connected once a token has been exchanged.
    /// </summary>
    public class SamsungTvIntegration : IDeviceIntegration
    {
        public string Id => "samsung-tv";
        public string DisplayName => "Samsung Smart TV (not paired)";
        public IntegrationState State => IntegrationState.Unlinked;
        public bool Supports(DeviceType type) => type == DeviceType.Television;

        /// <summary>Nothing is supported until pairing exists, and the console shows that plainly.</summary>
        public bool SupportsCommand(Device device, string commandKey) => false;

        public IntegrationResult Execute(Device device, string commandKey)
            => IntegrationResult.Refused(
                "No pairing has been completed with this television, so NEXUS will not claim to have sent \""
                + commandKey + "\". Switch the device to the simulation backend, or implement pairing first.");
    }

    /// <summary>Registry of available backends. Resolution falls back to simulation.</summary>
    public class IntegrationManager
    {
        private readonly Dictionary<string, IDeviceIntegration> _byId =
            new Dictionary<string, IDeviceIntegration>(StringComparer.OrdinalIgnoreCase);

        public IntegrationManager()
        {
            Register(new SimulatedIntegration());
            Register(new SamsungTvIntegration());
        }

        public IEnumerable<IDeviceIntegration> All => _byId.Values;

        public void Register(IDeviceIntegration integration)
        {
            if (integration == null) return;
            _byId[integration.Id] = integration;
        }

        public IDeviceIntegration Resolve(Device device)
        {
            if (device != null && !string.IsNullOrWhiteSpace(device.IntegrationId) &&
                _byId.TryGetValue(device.IntegrationId, out var found))
                return found;

            return _byId["simulated"];
        }
    }
}

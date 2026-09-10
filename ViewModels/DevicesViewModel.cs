using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using NEXUS.Common;
using NEXUS.Models;
using NEXUS.Services;

namespace NEXUS.ViewModels
{
    public class DevicesViewModel : ViewModelBase
    {
        private readonly DeviceManager _devices;
        private readonly EventBus _bus;
        private readonly ListCollectionView _view;

        private Device _selected;
        private string _search = "";
        private string _filter = "ALL";
        private string _lastResult = "";

        public DevicesViewModel()
        {
            _devices = AppServices.Devices;
            _bus = AppServices.Bus;

            _view = new ListCollectionView(_devices.Devices) { Filter = Match };
            _devices.Changed += Refresh;

            ExecuteCommand = new RelayCommand(p => Execute(p as DeviceCommand));
            SelectCommand = new RelayCommand(p => { if (p is Device d) Selected = d; });
            SetFilterCommand = new RelayCommand(p => Filter = p?.ToString() ?? "ALL");
            ToggleStatusCommand = new RelayCommand(_ => ToggleStatus(), _ => Selected != null);
            RemoveCommand = new RelayCommand(_ => RemoveSelected(), _ => Selected != null && Selected.IsDiscovered);
            SweepCommand = new RelayCommand(_ => _devices.RunHealthCheck());
            DiscoverCommand = new RelayCommand(_ => Discover());

            Selected = _devices.Devices.FirstOrDefault();
        }

        public ICollectionView Devices => _view;

        public ICommand ExecuteCommand { get; }
        public ICommand SelectCommand { get; }
        public ICommand SetFilterCommand { get; }
        public ICommand ToggleStatusCommand { get; }
        public ICommand RemoveCommand { get; }
        public ICommand SweepCommand { get; }
        public ICommand DiscoverCommand { get; }

        public IReadOnlyList<string> Filters { get; } = new[] { "ALL", "ONLINE", "OFFLINE", "CONTROLLABLE", "DISCOVERED" };

        public Device Selected
        {
            get => _selected;
            set
            {
                if (Set(ref _selected, value))
                {
                    LastResult = "";
                    LoadDeviceEvents();
                    OnPropertiesChanged(nameof(HasSelection), nameof(SelectedControls),
                                        nameof(ControlsEnabled), nameof(SelectedNotice),
                                        nameof(SupportedControlCount), nameof(ControlAvailabilityLabel));
                }
            }
        }

        public bool HasSelection => Selected != null;

        /// <summary>Controls for the selected device, each flagged with whether it truly works.</summary>
        public IEnumerable<DeviceCommand> SelectedControls
        {
            get
            {
                if (Selected == null) return Enumerable.Empty<DeviceCommand>();

                var controls = Selected.Controls.ToList();
                foreach (var control in controls)
                    control.IsSupported = _devices.Supports(Selected, control.Key);
                return controls;
            }
        }

        /// <summary>Audit entries mentioning this device, shown in its console.</summary>
        public ObservableCollection<SystemEvent> SelectedEvents { get; } = new ObservableCollection<SystemEvent>();

        public int SupportedControlCount => SelectedControls.Count(c => c.IsSupported);

        public string ControlAvailabilityLabel
        {
            get
            {
                if (Selected == null) return "";
                var total = SelectedControls.Count();
                var usable = SupportedControlCount;
                if (usable == 0) return "NO CONTROLS AVAILABLE";
                return usable + " OF " + total + " CONTROLS AVAILABLE";
            }
        }

        /// <summary>Controls stay live for simulated devices, but are disabled where NEXUS cannot act at all.</summary>
        public bool ControlsEnabled => Selected != null &&
            Selected.Integration != IntegrationState.NotConfigured &&
            Selected.Integration != IntegrationState.Observed;

        public string SelectedNotice => Selected?.IntegrationNotice ?? "";

        public string LastResult
        {
            get => _lastResult;
            private set => Set(ref _lastResult, value);
        }

        public string Search
        {
            get => _search;
            set { if (Set(ref _search, value)) Refresh(); }
        }

        public string Filter
        {
            get => _filter;
            set { if (Set(ref _filter, value)) Refresh(); }
        }

        public int OnlineCount => _devices.OnlineCount;
        public int OfflineCount => _devices.OfflineCount;
        public int TotalCount => _devices.Total;
        public string Summary => OnlineCount + " ONLINE / " + OfflineCount + " OFFLINE";

        public override void OnActivated() => Refresh();

        public override void OnTick()
        {
            foreach (var d in _devices.Devices) d.RefreshTimeDependent();
        }

        /// <summary>Called by global search when a device result is chosen.</summary>
        public void SelectPayload(object payload)
        {
            if (payload is Device d) Selected = d;
        }

        private void Execute(DeviceCommand command)
        {
            if (command == null || Selected == null) return;

            var result = _devices.Execute(Selected, command.Key);
            LastResult = result.Message;

            AppServices.Toasts.Show(
                result.Handled ? Selected.Name.ToUpperInvariant() : "COMMAND REFUSED",
                result.Message,
                result.Handled ? ToastKind.Info : ToastKind.Warning);

            LoadDeviceEvents();
            OnPropertiesChanged(nameof(SelectedControls), nameof(SupportedControlCount),
                                nameof(ControlAvailabilityLabel));
        }

        /// <summary>Pulls this device's slice of the central audit trail.</summary>
        private void LoadDeviceEvents()
        {
            SelectedEvents.Clear();
            if (Selected == null) return;

            foreach (var e in _bus.Events
                         .Where(e => (e.Message ?? "").IndexOf(Selected.Name, StringComparison.OrdinalIgnoreCase) >= 0
                                  || (e.Detail ?? "").IndexOf(Selected.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                         .Take(10))
                SelectedEvents.Add(e);
        }

        private void ToggleStatus()
        {
            if (Selected == null) return;
            _devices.SetStatus(Selected,
                Selected.Status == DeviceStatus.Online ? DeviceStatus.Offline : DeviceStatus.Online);
            Refresh();
        }

        private void RemoveSelected()
        {
            var target = Selected;
            if (target == null) return;

            if (!DialogService.Confirm("REMOVE DEVICE",
                    "\"" + target.Name + "\" will be removed from the NEXUS inventory.", "REMOVE"))
                return;

            _devices.Remove(target);
            Selected = _devices.Devices.FirstOrDefault();
        }

        private void Discover()
        {
            var added = AppServices.Network.DiscoverFromArpTable();
            AppServices.Toasts.Show("NEIGHBOUR SCAN COMPLETE",
                added == 0 ? "No new hosts in the local ARP cache." : added + " host(s) registered.",
                ToastKind.Success);
            Refresh();
        }

        private void Refresh()
        {
            _view.Refresh();
            OnPropertiesChanged(nameof(OnlineCount), nameof(OfflineCount), nameof(TotalCount), nameof(Summary));
        }

        private bool Match(object item)
        {
            if (item is not Device d) return false;

            switch (Filter)
            {
                case "ONLINE": if (!d.IsOnline) return false; break;
                case "OFFLINE": if (d.IsOnline) return false; break;
                case "CONTROLLABLE":
                    if (d.Integration != IntegrationState.Simulated &&
                        d.Integration != IntegrationState.Connected) return false;
                    break;
                case "DISCOVERED": if (!d.IsDiscovered) return false; break;
            }

            if (string.IsNullOrWhiteSpace(Search)) return true;

            var q = Search.Trim();
            return Contains(d.Name, q) || Contains(d.IpAddress, q) || Contains(d.TypeLabel, q)
                || Contains(d.Vendor, q) || Contains(d.MacAddress, q);
        }

        private static bool Contains(string haystack, string needle)
            => !string.IsNullOrEmpty(haystack) &&
               haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}

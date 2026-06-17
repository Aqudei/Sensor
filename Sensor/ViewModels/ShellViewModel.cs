using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sensor.Models;
using Sensor.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using System.Windows.Navigation;

namespace Sensor.ViewModels
{
    public partial class ShellViewModel : ObservableObject
    {
        private readonly InventorySyncService _syncService;

        // Form inputs bound to the View
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SubmitTelemetryCommand))]
        private string _endUser = string.Empty;


        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(FetchParametersCommand))]
        private string _apiUrl;


        // Status message for the UI footer
        [ObservableProperty]
        private string _statusText = "Ready to audit.";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(FetchParametersCommand))]
        private string _groupKey = string.Empty;


        // Track state lookup selections using the underlying ID value strings
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SubmitTelemetryCommand))]
        private string _selectedUnitId;
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SubmitTelemetryCommand))]
        private string _selectedNetworkTypeId;
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SubmitTelemetryCommand))]
        private string _selectedDeviceTypeId;


        // Data-binding collections for the ComboBox items
        public ObservableCollection<ContextLookupItem> Units { get; } = new();
        public ObservableCollection<ContextLookupItem> NetworkTypes { get; } = new();
        public ObservableCollection<ContextLookupItem> DeviceTypes { get; } = new();


        // Structural loading state flag
        [ObservableProperty] private bool _isContextLoading;

        public ShellViewModel()
        {
            // Initializing the network service abstraction layered in our previous migration
            _syncService = new InventorySyncService(Properties.Settings.Default.API_URL, Properties.Settings.Default.GROUP_KEY);

            GroupKey = Properties.Settings.Default.GROUP_KEY;
            EndUser = Properties.Settings.Default.END_USER;
            ApiUrl = Properties.Settings.Default.API_URL;
        }



        public async Task LoadFormSchemasAsync()
        {
            IsContextLoading = true;

            // Fire all network endpoints in parallel (Equivalent to ThreadPoolExecutor in Python)
            var unitsTask = _syncService.GetUnitsAsync();
            var networksTask = _syncService.GetNetworkTypesAsync();
            var devicesTask = _syncService.GetDeviceTypesAsync();

            await Task.WhenAll(unitsTask, networksTask, devicesTask);

            // Populate our collections bound to UI components safely on the main thread dispatch context
            Units.Clear();
            foreach (var item in await unitsTask) Units.Add(item);

            NetworkTypes.Clear();
            foreach (var item in await networksTask) NetworkTypes.Add(item);

            DeviceTypes.Clear();
            foreach (var item in await devicesTask) DeviceTypes.Add(item);

            IsContextLoading = false;
        }


        [RelayCommand(CanExecute = nameof(CanFetchParameters))]
        private async Task FetchParameters()
        {
            _syncService.UpdateCreds(ApiUrl, GroupKey);
            Properties.Settings.Default.END_USER = EndUser;
            Properties.Settings.Default.API_URL = ApiUrl;
            Properties.Settings.Default.GROUP_KEY = GroupKey;
            Properties.Settings.Default.Save();

            await LoadFormSchemasAsync();

            if (!string.IsNullOrEmpty(Properties.Settings.Default.DEVICE_TYPE))
                SelectedDeviceTypeId = Properties.Settings.Default.DEVICE_TYPE;
            if (!string.IsNullOrEmpty(Properties.Settings.Default.NETWORK_TYPE))
                SelectedNetworkTypeId = Properties.Settings.Default.NETWORK_TYPE;
            if (!string.IsNullOrEmpty(Properties.Settings.Default.UNIT))
                SelectedUnitId = Properties.Settings.Default.UNIT;
        }

        [RelayCommand(CanExecute = nameof(CanSubmit))]
        private async Task SubmitTelemetry()
        {
            StatusText = "Gathering local hardware profile metrics...";

            // Execute the native profile reads off the main UI thread
            var payload = await Task.Run(() =>
            {
                string biosSerial = SystemTelemetry.GetBiosSerial();
                string machineGuid = SystemTelemetry.GetMachineGuid();

                return new DevicePayload
                {
                    DeviceName = Environment.MachineName,
                    OperatingSystem = Environment.OSVersion.ToString(),
                    MsOffice = SystemTelemetry.GetWindowsOfficeVersion(),
                    BiosSerial = biosSerial,
                    Model = SystemTelemetry.GetSystemModel(),
                    AVEDR = string.Join('/', SystemTelemetry.CheckEDRServices()),
                    SerialNumber = !string.IsNullOrEmpty(machineGuid) ? machineGuid : biosSerial,
                    Interfaces = SystemTelemetry.GetNetworkInterfaces(),
                    EndUser = EndUser,
                    Unit = SelectedUnitId,
                    NetworkType = SelectedNetworkTypeId,
                    DeviceType = SelectedDeviceTypeId
                };
            });

            StatusText = "Transmitting metrics to enterprise vault API endpoint...";

            // Send payload using our network service layer
            bool success = await _syncService.PostPayloadAsync(payload);

            StatusText = success
                ? "Synchronization Complete! Hardware state recorded successfully."
                : "Transmission Error. Check logs for details.";

            if (success)
            {
                Properties.Settings.Default.NETWORK_TYPE = SelectedNetworkTypeId;
                Properties.Settings.Default.DEVICE_TYPE = SelectedDeviceTypeId;
                Properties.Settings.Default.UNIT = SelectedUnitId;
                Properties.Settings.Default.END_USER = EndUser;
                Properties.Settings.Default.API_URL = ApiUrl;
                Properties.Settings.Default.GROUP_KEY = GroupKey;

                Properties.Settings.Default.Save();
            }
        }

        private bool CanSubmit()
        {
            return !string.IsNullOrWhiteSpace(EndUser) &&
                   !string.IsNullOrWhiteSpace(SelectedUnitId) &&
                   !string.IsNullOrWhiteSpace(SelectedNetworkTypeId) &&
                   !string.IsNullOrWhiteSpace(SelectedDeviceTypeId);
        }

        private bool CanFetchParameters()
        {
            return !string.IsNullOrWhiteSpace(GroupKey) &&
                !string.IsNullOrWhiteSpace(ApiUrl);
        }
    }
}

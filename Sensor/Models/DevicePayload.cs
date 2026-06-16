using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Sensor.Models
{
    public class DevicePayload
    {
        [JsonPropertyName("operating_system")]
        public string OperatingSystem { get; set; }

        [JsonPropertyName("ms_office")]
        public string MsOffice { get; set; }

        [JsonPropertyName("serial_number")]
        public string SerialNumber { get; set; }

        [JsonPropertyName("bios_serial")]
        public string BiosSerial { get; set; }

        [JsonPropertyName("interfaces")]
        public List<NetworkInterfaceModel> Interfaces { get; set; }

        [JsonPropertyName("device_name")]
        public string DeviceName { get; set; }

        [JsonPropertyName("end_user")]
        public string EndUser { get; set; }

        [JsonPropertyName("unit")]
        public string Unit { get; set; }

        [JsonPropertyName("device_type")]
        public string DeviceType { get; set; }

        [JsonPropertyName("network_type")]
        public string NetworkType { get; set; }
    }
}

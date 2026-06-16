using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Sensor.Models
{
    public class NetworkInterfaceModel
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("mac_address")]
        public string MacAddress { get; set; }

        [JsonPropertyName("ip_address")]
        public string IpAddress { get; set; }
    }
}

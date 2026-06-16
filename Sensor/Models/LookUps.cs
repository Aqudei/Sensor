using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json.Serialization;

namespace Sensor.Models
{
    public class ContextLookupItem
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        public override string ToString()
        {
            return $"{Id} - {Name}";
        }
    }

    public class ApiResponseWrapper
    {
        [JsonPropertyName("results")]
        public List<ContextLookupItem> Results { get; set; } = new();
    }
}

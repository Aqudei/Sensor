using Sensor.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Sensor.Services
{
    public class InventorySyncService
    {
        private HttpClient _httpClient;

        public InventorySyncService(string baseAddress, string groupKey)
        {
            if (string.IsNullOrEmpty(baseAddress) || string.IsNullOrEmpty(groupKey))
                return;

            _httpClient = new HttpClient { BaseAddress = new Uri(baseAddress) };
            _httpClient.DefaultRequestHeaders.Add("GROUP-KEY", groupKey);
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }


        /// <summary>
        /// Fetches organizational units from the remote infrastructure server.
        /// </summary>
        public async Task<List<ContextLookupItem>> GetUnitsAsync()
        {
            return await FetchLookupDataAsync("api/inventory/units");
        }

        /// <summary>
        /// Fetches valid network context profiles from the remote infrastructure server.
        /// </summary>
        public async Task<List<ContextLookupItem>> GetNetworkTypesAsync()
        {
            return await FetchLookupDataAsync("api/inventory/network-types");
        }

        /// <summary>
        /// Fetches valid device architecture layout definitions from the remote infrastructure server.
        /// </summary>
        public async Task<List<ContextLookupItem>> GetDeviceTypesAsync()
        {
            return await FetchLookupDataAsync("api/inventory/device-types");
        }


        /// <summary>
        /// Common internal abstraction to handle JSON unpacking and edge-case exceptions.
        /// </summary>
        private async Task<List<ContextLookupItem>> FetchLookupDataAsync(string relativeUrl)
        {
            try
            {
                var response = await _httpClient.GetFromJsonAsync<ApiResponseWrapper>(relativeUrl);
                return response?.Results ?? new List<ContextLookupItem>();
            }
            catch (HttpRequestException)
            {
                // Fallback gracefully to an empty list or bubble up exception depending on enterprise logging strategies
                return new List<ContextLookupItem>();
            }
            catch (Exception)
            {
                return new List<ContextLookupItem>();
            }
        }
        public async Task<bool> PostPayloadAsync(DevicePayload payload)
        {
            const string targetPostUrl = "api/inventory/devices/";
            try
            {
                HttpResponseMessage response = await _httpClient.PostAsJsonAsync(targetPostUrl, payload);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                // --- Error Diagnostic Extraction Block ---
                Debug.WriteLine($"\n[API Error Response] Status Code: {(int)response.StatusCode} ({response.StatusCode})");

                string errorContent = await response.Content.ReadAsStringAsync();

                if (!string.IsNullOrWhiteSpace(errorContent))
                {
                    try
                    {
                        // Attempt to pretty-print the JSON error details (equivalent to pprint in Python)
                        using var jsonDoc = JsonDocument.Parse(errorContent);
                        string formattedJson = JsonSerializer.Serialize(jsonDoc, new JsonSerializerOptions { WriteIndented = true });
                        Debug.WriteLine(formattedJson);
                    }
                    catch (JsonException)
                    {
                        // Fallback to raw string text if the server didn't return valid JSON
                        Debug.WriteLine($"Raw Response Output: {errorContent}");
                    }
                }
                else
                {
                    Debug.WriteLine("The server response body was empty.");
                }
                // ------------------------------------------

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"\nNetwork transport layer or local exception occurred: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Executes a POST transmission sync operation against the system management API context endpoint.
        /// </summary>
        public async Task ProcessSendOnlyAsync(DevicePayload payload)
        {
            if (payload == null)
            {
                Debug.WriteLine("\nError: No cached configuration payload found for this user context configuration.");
                Environment.Exit(1);
            }

            const string targetPostUrl = "api/inventory/devices/";

            try
            {
                HttpResponseMessage response = await _httpClient.PostAsJsonAsync(targetPostUrl, payload);
                response.EnsureSuccessStatusCode();

                string jsonResponse = await response.Content.ReadAsStringAsync();
                Debug.WriteLine("\nTransmission Successful. API Endpoint Sync State Response:");

                // Format json response nicely for visualization parsing equivalent to pprint
                using var doc = JsonDocument.Parse(jsonResponse);
                Debug.WriteLine(JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));

                Environment.Exit(0);
            }
            catch (HttpRequestException httpEx)
            {
                Debug.WriteLine($"\nAPI Error Response (Status Code: {httpEx.StatusCode}):");
                Debug.WriteLine(httpEx.Message);
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"\nAn unexpected runtime error occurred: {ex.Message}");
                Environment.Exit(1);
            }
        }

        internal void UpdateCreds(string baseAddress, string groupKey)
        {
            _httpClient = new HttpClient { BaseAddress = new Uri(baseAddress) };
            _httpClient.DefaultRequestHeaders.Add("GROUP-KEY", groupKey);
            _httpClient.Timeout = TimeSpan.FromSeconds(10);
        }
    }
}

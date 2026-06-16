using Microsoft.Win32;
using Sensor.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace Sensor.Services
{
    public static class SystemTelemetry
    {
        /// <summary>
        /// Generates a tracking key combining Computer Name and Current User.
        /// </summary>
        public static string GetUserHostKey()
        {
            string computerName = Environment.MachineName;
            string userName = Environment.UserName;
            return $"{computerName}\\{userName}";
        }

        /// <summary>
        /// Retrieves the Windows MachineGuid from the registry. Returns null if not on Windows.
        /// </summary>
        public static string GetMachineGuid()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return null;
            }

            const string registryPath = @"SOFTWARE\Microsoft\Cryptography";
            try
            {
                // Explicitly open the 64-bit view of the registry to match KEY_WOW64_64KEY
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var subKey = baseKey.OpenSubKey(registryPath))
                {
                    var value = subKey?.GetValue("MachineGuid");
                    return value?.ToString()?.Trim();
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Scans the registry for installed Microsoft Office editions.
        /// </summary>
        public static string GetWindowsOfficeVersion()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "Not applicable on this Operating System.";
            }

            var officeVersions = new Dictionary<string, string>
        {
            { "16.0", "Office 2016 / 2019 / 2021 / 365" },
            { "15.0", "Office 2013" },
            { "14.0", "Office 2010" },
            { "12.0", "Office 2007" }
        };

            string[] registryPaths =
            {
            @"SOFTWARE\Microsoft\Office\ClickToRun\Configuration",
            @"SOFTWARE\Microsoft\Office"
        };

            RegistryView[] views = { RegistryView.Registry64, RegistryView.Registry32 };

            foreach (var view in views)
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                {
                    foreach (var path in registryPaths)
                    {
                        try
                        {
                            using (var key = baseKey.OpenSubKey(path))
                            {
                                if (key == null) continue;

                                if (path.Contains("ClickToRun"))
                                {
                                    var prodId = key.GetValue("ProductReleaseIds");
                                    if (prodId != null)
                                    {
                                        return $"Microsoft 365 / ClickToRun ({prodId})";
                                    }
                                }

                                foreach (var subKeyName in key.GetSubKeyNames())
                                {
                                    if (officeVersions.TryGetValue(subKeyName, out string versionDescription))
                                    {
                                        return versionDescription;
                                    }
                                    ;
                                }
                            }
                        }
                        catch (UnauthorizedAccessException) { continue; }
                        catch (IOException) { continue; }
                    }
                }
            }

            return "Microsoft Office not detected via standard registry paths.";
        }

        /// <summary>
        /// Resolves the hardware BIOS Serial Number across Windows, Linux, and macOS.
        /// </summary>
        public static string GetBiosSerial()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Primary method: PowerShell CimInstance
                string serial = RunCommand("powershell", "-NoProfile -Command \"Get-CimInstance -ClassName Win32_BIOS | Select-Object -ExpandProperty SerialNumber\"");
                if (!string.IsNullOrWhiteSpace(serial)) return serial.Trim();

                // Fallback method: WMIC
                string wmicOut = RunCommand("wmic", "bios get serialnumber");
                if (!string.IsNullOrWhiteSpace(wmicOut))
                {
                    var lines = wmicOut.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(l => l.Trim())
                                       .ToList();
                    if (lines.Count >= 2) return lines[1];
                    if (lines.Count > 0) return lines[0];
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                const string sysSerialPath = "/sys/class/dmi/id/product_serial";
                if (File.Exists(sysSerialPath))
                {
                    try
                    {
                        string text = File.ReadAllText(sysSerialPath).Trim();
                        if (!string.IsNullOrEmpty(text) &&
                            !text.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                            !text.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
                            !(text != "0"))
                        {
                            return text;
                        }
                    }
                    catch { }
                }

                string dmidecodeOut = RunCommand("sudo", "dmidecode -s system-serial-number");
                if (!string.IsNullOrWhiteSpace(dmidecodeOut))
                {
                    string cleaned = dmidecodeOut.Trim();
                    if (!string.IsNullOrEmpty(cleaned) &&
                        !cleaned.Equals("none", StringComparison.OrdinalIgnoreCase) &&
                        !cleaned.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
                        cleaned != "0")
                    {
                        return cleaned;
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                string ioregOut = RunCommand("ioreg", "-c IOPlatformExpertDevice -r -l");
                if (!string.IsNullOrWhiteSpace(ioregOut))
                {
                    foreach (var line in ioregOut.Split('\n'))
                    {
                        if (line.Contains("IOPlatformSerialNumber"))
                        {
                            var parts = line.Split('=');
                            if (parts.Length >= 2)
                            {
                                return parts[1].Replace("\"", "").Trim();
                            }
                        }
                    }
                }

                string profilerOut = RunCommand("system_profiler", "SPHardwareDataType");
                if (!string.IsNullOrWhiteSpace(profilerOut))
                {
                    foreach (var line in profilerOut.Split('\n'))
                    {
                        if (line.Contains("Serial Number"))
                        {
                            var parts = line.Split(':');
                            if (parts.Length >= 2) return parts[1].Trim();
                        }
                    }
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Retrieves a list of active network interfaces matching MAC and IP address pairs.
        /// </summary>
        internal static List<NetworkInterfaceModel> GetNetworkInterfaces()
        {
            var result = new List<NetworkInterfaceModel>();
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var ni in interfaces)
            {
                // Skip loopback or down interfaces to align closely with standard infrastructure audits
                if (ni.OperationalStatus != OperationalStatus.Up) continue;

                string macAddress = ni.GetPhysicalAddress().ToString();
                // Format MAC address into pairs if needed, or leave raw to match Python psutil footprint
                if (string.IsNullOrEmpty(macAddress)) continue;

                string ipAddress = null;
                var ipProperties = ni.GetIPProperties();

                foreach (var unicast in ipProperties.UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily == AddressFamily.InterNetwork) // IPv4
                    {
                        ipAddress = unicast.Address.ToString();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(ipAddress) || string.IsNullOrWhiteSpace(ni.Name)) 
                    continue;

                result.Add(new NetworkInterfaceModel
                {
                    Name = ni.Name,
                    MacAddress = macAddress,
                    IpAddress = ipAddress
                });
            }

            return result;
        }

        /// <summary>
        /// Helper routine executing system sub-processes safely under an isolated execution window.
        /// </summary>
        private static string RunCommand(string fileName, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    //Timeout = 5000 // Requires .NET 7+ for explicit integration via system architecture, else managed via timer loop
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();
                    // Basic timeout management mechanism matching Python's timeout=5
                    if (!process.WaitForExit(5000))
                    {
                        process.Kill();
                        return null;
                    }
                    string output = process.StandardOutput.ReadToEnd();
                    return !string.IsNullOrWhiteSpace(output) ? output.Trim() : null;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}

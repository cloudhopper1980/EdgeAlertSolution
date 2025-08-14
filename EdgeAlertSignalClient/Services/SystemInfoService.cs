using System.Net;
using System.Management;
using EdgeAlertSignalClient.Handlers;
using System;
using System.Net.NetworkInformation;
using System.Text;
using System.Security.Cryptography;
using System.Linq;

namespace EdgeAlertSignalClient.Services
{
    public class SystemInfoService
    {
        public string? HostName { get; set; }
        public string? SerialNumber { get; set; }
        public string? IpAddress { get; set; }
        public string? OperatingSystem { get; set; }
        public string? HardDriveTotalSpace { get; set; }
        public string? HardDriveFreeSpace { get; set; }
        public string? RamCapacity { get; set; }
        public string? Manufacturer { get; set; }
        public string? Model { get; set; }
        public string? ODS { get; set; }
        public string? Room { get; set; }
        public string? UserName { get; set; }
        public string? LoggedIn { get; set; }
        public string? LastOnline { get; set; }
        public string? Version { get; set; }

        private static string ConvertToGB(UInt64? sizeInBytes)
        {
            if (sizeInBytes.HasValue)
            {
                return $"{Math.Round((double)sizeInBytes.Value / (1024 * 1024 * 1024), 2)} GB";
            }
            return null;
        }

        public static SystemInfoService GetSystemInfo()
        {
            var info = new SystemInfoService();

            // HostName and IP
            info.HostName = Dns.GetHostName();
            info.IpAddress = IPAddressHandler.GetTrustedIPAddress().ToString();

            // Serial Number, Manufacturer, and Model
            foreach (ManagementObject queryObj in new ManagementObjectSearcher("SELECT * FROM Win32_BIOS").Get())
            {
                info.SerialNumber = (string)queryObj["SerialNumber"];
            }

            foreach (ManagementObject queryObj in new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem").Get())
            {
                info.Manufacturer = (string)queryObj["Manufacturer"];
                info.Model = (string)queryObj["Model"];
            }

            // If SerialNumber is null or empty, generate a fallback unique ID
            if (string.IsNullOrEmpty(info.SerialNumber))
            {
                info.SerialNumber = GenerateUniqueMachineId();
            }

            // Operating System
            foreach (ManagementObject queryObj in new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem").Get())
            {
                string caption = (string)queryObj["Caption"];
                string version = (string)queryObj["Version"];
                string buildNumber = (string)queryObj["BuildNumber"];
                string servicePack = (string)queryObj["CSDVersion"]; // Service Pack if applicable

                info.OperatingSystem = $"{caption}: Version {version} (OS Build {buildNumber})";
                if (!string.IsNullOrEmpty(servicePack))
                {
                    info.OperatingSystem += $" {servicePack}";
                }
            }

            // Hard Drive Information
            foreach (ManagementObject queryObj in new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDisk WHERE DriveType=3 AND DeviceID='C:'").Get())
            {
                info.HardDriveTotalSpace = ConvertToGB((UInt64?)queryObj["Size"]);
                info.HardDriveFreeSpace = ConvertToGB((UInt64?)queryObj["FreeSpace"]);
            }

            // RAM Information
            foreach (ManagementObject queryObj in new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem").Get())
            {
                info.RamCapacity = ConvertToGB((UInt64?)queryObj["TotalPhysicalMemory"]);
            }

            return info;
        }

        public static string GenerateUniqueMachineId()
        {
            var components = new StringBuilder();

            // HostName
            components.Append(Dns.GetHostName());

            // MAC Address
            var macAddresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
                .Select(nic => nic.GetPhysicalAddress().ToString());
            components.Append(string.Join("", macAddresses));

            // Manufacturer and Model
            foreach (ManagementObject queryObj in new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem").Get())
            {
                components.Append(queryObj["Manufacturer"]);
                components.Append(queryObj["Model"]);
            }

            // Hash the combined components
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(components.ToString()));
                return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16); // Shorten to 16 characters for convenience
            }
        }
    }
}

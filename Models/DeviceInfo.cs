using System;

namespace PhoneRomFlashTool.Models
{
    public class DeviceInfo
    {
        public string DeviceId { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string AndroidVersion { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public DeviceConnectionType ConnectionType { get; set; }
        public DeviceMode Mode { get; set; }
        public string PortName { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
        public DateTime ConnectedTime { get; set; }
        public string Chipset { get; set; } = string.Empty;
        public long StorageSize { get; set; }
        public string BootloaderStatus { get; set; } = string.Empty;

        // USB connection information
        public string UsbType { get; set; } = "USB-A";  // USB-A, USB-C, Thunderbolt
        public string UsbSpeed { get; set; } = "USB 2.0";  // USB 2.0, USB 3.0+, USB 3.1, USB 3.2

        public string DisplayName => $"{Brand} {Model} ({SerialNumber})";
        public string ConnectionInfo => $"{UsbType} ({UsbSpeed})";
    }

    public enum DeviceConnectionType
    {
        Unknown,
        ADB,
        Fastboot,
        EDL,
        MTK,
        Samsung,
        Qualcomm
    }

    public enum DeviceMode
    {
        Unknown,
        Normal,
        Recovery,
        Fastboot,
        Download,
        EDL,
        Bootloader
    }
}

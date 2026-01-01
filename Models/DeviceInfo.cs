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

        public string DisplayName => $"{Brand} {Model} ({SerialNumber})";
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

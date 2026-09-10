using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace NEXUS.Services
{
    public class StorageVolume
    {
        public string Name { get; set; } = "";
        public string Label { get; set; } = "";
        public string Format { get; set; } = "";
        public long TotalBytes { get; set; }
        public long FreeBytes { get; set; }

        public double UsedFraction => TotalBytes <= 0 ? 0 : 1.0 - (double)FreeBytes / TotalBytes;
        public string UsedPercent => (int)Math.Round(UsedFraction * 100) + "%";
        public string TotalDisplay => SystemInfoService.FormatBytes(TotalBytes);
        public string FreeDisplay => SystemInfoService.FormatBytes(FreeBytes);
        public string UsedDisplay => SystemInfoService.FormatBytes(TotalBytes - FreeBytes);

        public string BrushKey => UsedFraction > 0.9 ? "BrushCritical"
            : UsedFraction > 0.75 ? "BrushHigh" : "BrushAccent";
    }

    /// <summary>
    /// Reads genuine telemetry for the machine NEXUS is running on, using only the
    /// Windows APIs that ship with the OS. No third-party packages, no WMI dependency.
    /// </summary>
    public class SystemInfoService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

        private long _prevIdle, _prevKernel, _prevUser;
        private bool _hasBaseline;
        private int _storageTick;

        public double CpuLoad { get; private set; }
        public double MemoryLoad { get; private set; }
        public long TotalMemoryBytes { get; private set; }
        public long AvailableMemoryBytes { get; private set; }
        public List<StorageVolume> Volumes { get; } = new List<StorageVolume>();

        /// <summary>Rolling window of recent processor samples, oldest first, for the sparkline.</summary>
        public List<double> CpuHistory { get; } = new List<double>();

        /// <summary>Rolling window of recent memory samples.</summary>
        public List<double> MemoryHistory { get; } = new List<double>();

        public bool HasBattery { get; private set; }
        public int BatteryPercent { get; private set; }
        public bool IsOnMains { get; private set; }

        public string BatteryDisplay => HasBattery ? BatteryPercent + "%" : "NO BATTERY";
        public string PowerSourceLabel => !HasBattery ? "AC POWER" : IsOnMains ? "CHARGING" : "ON BATTERY";

        public string BatteryBrushKey => !HasBattery ? "BrushTextMuted"
            : BatteryPercent < 15 ? "BrushCritical"
            : BatteryPercent < 35 ? "BrushHigh" : "BrushCompleted";

        public string MachineName => Environment.MachineName;
        public string UserName => Environment.UserName;
        public string OperatingSystem => RuntimeInformation.OSDescription;
        public string Architecture => RuntimeInformation.OSArchitecture.ToString();
        public int LogicalProcessors => Environment.ProcessorCount;
        public string Runtime => RuntimeInformation.FrameworkDescription;

        public string CpuPercent => (int)Math.Round(CpuLoad * 100) + "%";
        public string MemoryPercent => (int)Math.Round(MemoryLoad * 100) + "%";
        public string TotalMemoryDisplay => FormatBytes(TotalMemoryBytes);
        public string UsedMemoryDisplay => FormatBytes(TotalMemoryBytes - AvailableMemoryBytes);
        public string AvailableMemoryDisplay => FormatBytes(AvailableMemoryBytes);

        public double StorageLoad => Volumes.Count == 0 ? 0 : Volumes.Average(v => v.UsedFraction);
        public string StoragePercent => (int)Math.Round(StorageLoad * 100) + "%";

        public TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);

        public string UptimeDisplay
        {
            get
            {
                var u = Uptime;
                if (u.TotalDays >= 1) return (int)u.TotalDays + "D " + u.Hours + "H " + u.Minutes + "M";
                return u.Hours.ToString("00") + ":" + u.Minutes.ToString("00") + ":" + u.Seconds.ToString("00");
            }
        }

        public string BootTimeDisplay => DateTime.Now.Subtract(Uptime).ToString("dd MMM yyyy HH:mm").ToUpperInvariant();

        public string CpuBrushKey => CpuLoad > 0.85 ? "BrushCritical" : CpuLoad > 0.6 ? "BrushHigh" : "BrushAccent";
        public string MemoryBrushKey => MemoryLoad > 0.9 ? "BrushCritical" : MemoryLoad > 0.75 ? "BrushHigh" : "BrushAccent";

        /// <summary>Samples CPU, memory and storage. Call on a timer; the CPU figure needs two samples.</summary>
        public void Sample()
        {
            SampleCpu();
            SampleMemory();
            SampleBattery();
            Record(CpuHistory, CpuLoad);
            Record(MemoryHistory, MemoryLoad);

            // Volume figures move slowly, and rebuilding the list every tick would
            // make the storage panel flicker, so it is refreshed roughly every 30 samples.
            if (_storageTick++ % 30 == 0) SampleStorage();
        }

        /// <summary>Forces an immediate storage re-read, for the explicit refresh button.</summary>
        public void SampleNow()
        {
            SampleCpu();
            SampleMemory();
            SampleBattery();
            SampleStorage();
            Record(CpuHistory, CpuLoad);
            Record(MemoryHistory, MemoryLoad);
        }

        private void SampleBattery()
        {
            try
            {
                if (!GetSystemPowerStatus(out var status)) return;

                // 128 means "no system battery"; 255 means the state is unknown.
                HasBattery = status.BatteryFlag != 128 && status.BatteryLifePercent <= 100;
                BatteryPercent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : 0;
                IsOnMains = status.ACLineStatus == 1;
            }
            catch
            {
                HasBattery = false;
            }
        }

        private void Record(List<double> series, double value)
        {
            series.Add(value);
            while (series.Count > 60) series.RemoveAt(0);
        }

        private void SampleCpu()
        {
            try
            {
                if (!GetSystemTimes(out var idle, out var kernel, out var user)) return;

                if (_hasBaseline)
                {
                    var idleDelta = idle - _prevIdle;
                    var kernelDelta = kernel - _prevKernel;
                    var userDelta = user - _prevUser;
                    var total = kernelDelta + userDelta;

                    // Kernel time already includes idle time, so busy = total - idle.
                    if (total > 0)
                        CpuLoad = Math.Max(0, Math.Min(1, (double)(total - idleDelta) / total));
                }

                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
                _hasBaseline = true;
            }
            catch { }
        }

        private void SampleMemory()
        {
            try
            {
                var status = new MemoryStatusEx();
                status.dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));

                if (!GlobalMemoryStatusEx(ref status)) return;

                TotalMemoryBytes = (long)status.ullTotalPhys;
                AvailableMemoryBytes = (long)status.ullAvailPhys;
                MemoryLoad = status.dwMemoryLoad / 100.0;
            }
            catch { }
        }

        private void SampleStorage()
        {
            try
            {
                Volumes.Clear();
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;

                    Volumes.Add(new StorageVolume
                    {
                        Name = drive.Name,
                        Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "LOCAL DISK" : drive.VolumeLabel,
                        Format = drive.DriveFormat,
                        TotalBytes = drive.TotalSize,
                        FreeBytes = drive.AvailableFreeSpace
                    });
                }
            }
            catch { }
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1L << 40) return Math.Round(bytes / (double)(1L << 40), 1) + " TB";
            if (bytes >= 1L << 30) return Math.Round(bytes / (double)(1L << 30), 1) + " GB";
            if (bytes >= 1L << 20) return Math.Round(bytes / (double)(1L << 20), 1) + " MB";
            if (bytes >= 1L << 10) return Math.Round(bytes / (double)(1L << 10), 1) + " KB";
            return bytes + " B";
        }
    }
}

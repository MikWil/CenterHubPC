using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;
using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace CenterHubNew.MVVM.Services
{
    public class SystemMonitorService : ISystemMonitorService, IDisposable
    {
        private readonly PerformanceCounter _cpuCounter;
        private readonly ManagementObjectSearcher _gpuSearcher;
        private readonly Computer _computer;
        private readonly ILogger<SystemMonitorService>? _logger;
        private bool _disposed;

        public SystemMonitorService(ILogger<SystemMonitorService>? logger = null)
        {
            _logger = logger;
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _gpuSearcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController");
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true
                };
                _computer.Open();
                _logger?.LogInformation("SystemMonitorService initialized successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize SystemMonitorService");
                throw;
            }
        }

        public async Task<SystemInfo> GetSystemInfoAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SystemMonitorService));

            return await Task.Run(() =>
            {
                try
                {
                    var info = new SystemInfo
                    {
                        CpuUsage = _cpuCounter.NextValue(),
                        GpuInfo = GetGpuInfo(),
                        MemoryInfo = GetMemoryInfo(),
                        Disks = GetDiskInfo(),
                        CpuTemperature = GetCpuTemperature(out float cpuMax),
                        CpuMaxTemperature = cpuMax
                    };
                    info.GpuInfo.GpuTemperature = GetGpuTemperature(out float gpuMax);
                    info.GpuInfo.GpuMaxTemperature = gpuMax;
                    
                    _logger?.LogDebug("System info retrieved successfully");
                    return info;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error retrieving system info");
                    throw;
                }
            });
        }

        private float GetCpuTemperature(out float maxTemp)
        {
            float coreTemp = -1;
            maxTemp = -1;
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.Cpu)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                        {
                            // Track the hottest reading as max
                            if (sensor.Value.Value > maxTemp)
                                maxTemp = sensor.Value.Value;

                            // Prefer "CPU Package" or "Core (Tctl/Tdie)" as the primary reading
                            if (sensor.Name.Contains("Package") || sensor.Name.Contains("Tctl") || sensor.Name.Contains("Tdie"))
                            {
                                coreTemp = sensor.Value.Value;
                            }
                            // Fallback: use first temperature sensor if no specific one found yet
                            else if (coreTemp < 0)
                            {
                                coreTemp = sensor.Value.Value;
                            }
                        }
                    }
                }
            }
            if (coreTemp < 0) maxTemp = -1;
            return coreTemp;
        }

        private float GetGpuTemperature(out float maxTemp)
        {
            float coreTemp = -1;
            maxTemp = -1;
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                        {
                            // Track the hottest reading as max (e.g. Hot Spot)
                            if (sensor.Value.Value > maxTemp)
                                maxTemp = sensor.Value.Value;

                            // Prefer "GPU Core" as the primary reading
                            if (sensor.Name.Contains("GPU Core"))
                            {
                                coreTemp = sensor.Value.Value;
                            }
                            // Fallback: use first temperature sensor if no specific one found yet
                            else if (coreTemp < 0)
                            {
                                coreTemp = sensor.Value.Value;
                            }
                        }
                    }
                }
            }
            if (coreTemp < 0) maxTemp = -1;
            return coreTemp;
        }

        private GpuInfo GetGpuInfo()
        {
            var gpuInfo = new GpuInfo();
            bool foundVram = false;
            foreach (var obj in _gpuSearcher.Get())
            {
                gpuInfo.Name = obj["Name"]?.ToString() ?? string.Empty;
                gpuInfo.DriverVersion = obj["DriverVersion"]?.ToString() ?? string.Empty;
                if (obj["AdapterRAM"] is ulong ram && ram > 0)
                {
                    gpuInfo.VideoMemory = (long)(ram / (1024 * 1024 * 1024)); // Convert to GB
                    foundVram = true;
                }
                break; // Get first GPU only
            }

            // Get GPU usage from LibreHardwareMonitor
            foreach (var hardware in _computer.Hardware)
            {
                if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("GPU Core"))
                        {
                            if (sensor.Value.HasValue)
                            {
                                gpuInfo.Usage = sensor.Value.Value;
                            }
                        }
                    }
                }
            }

            // Fallback: Try LibreHardwareMonitor for VRAM if WMI fails
            if (!foundVram)
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (hardware.HardwareType == HardwareType.GpuNvidia || hardware.HardwareType == HardwareType.GpuAmd)
                    {
                        hardware.Update();
                        foreach (var sensor in hardware.Sensors)
                        {
                            // LibreHardwareMonitor does not always expose VRAM, but if it does, it's usually as a Data sensor
                            if (sensor.SensorType.ToString() == "Data" && sensor.Name.ToLower().Contains("memory total"))
                            {
                                if (sensor.Value.HasValue && sensor.Value.Value > 0)
                                {
                                    gpuInfo.VideoMemory = (long)(sensor.Value.Value / 1024); // MB to GB
                                    foundVram = true;
                                    break;
                                }
                            }
                        }
                    }
                    if (foundVram) break;
                }
            }
            if (!foundVram) gpuInfo.VideoMemory = -1;
            return gpuInfo;
        }

        private MemoryInfo GetMemoryInfo()
        {
            var memoryInfo = new MemoryInfo();
            var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
            memoryInfo.TotalPhysicalMemory = (long)(computerInfo.TotalPhysicalMemory / (1024 * 1024 * 1024)); // Convert to GB
            memoryInfo.AvailablePhysicalMemory = (long)(computerInfo.AvailablePhysicalMemory / (1024 * 1024 * 1024)); // Convert to GB
            memoryInfo.UsedPhysicalMemory = memoryInfo.TotalPhysicalMemory - memoryInfo.AvailablePhysicalMemory;
            return memoryInfo;
        }

        private List<DiskInfo> GetDiskInfo()
        {
            var disks = new List<DiskInfo>();
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                disks.Add(new DiskInfo
                {
                    DriveLetter = drive.Name,
                    TotalSize = (long)(drive.TotalSize / (1024 * 1024 * 1024)),
                    AvailableSpace = (long)(drive.AvailableFreeSpace / (1024 * 1024 * 1024)),
                    UsedSpace = (long)((drive.TotalSize - drive.AvailableFreeSpace) / (1024 * 1024 * 1024))
                });
            }
            return disks;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _cpuCounter?.Dispose();
                    _gpuSearcher?.Dispose();
                    _computer?.Close();
                    _logger?.LogInformation("SystemMonitorService disposed successfully");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error disposing SystemMonitorService");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }
    }

    public class SystemInfo
    {
        public float CpuUsage { get; set; }
        public float CpuTemperature { get; set; }
        public float CpuMaxTemperature { get; set; }
        public GpuInfo GpuInfo { get; set; } = new();
        public MemoryInfo MemoryInfo { get; set; } = new();
        public List<DiskInfo> Disks { get; set; } = new();
    }

    public class GpuInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DriverVersion { get; set; } = string.Empty;
        public long VideoMemory { get; set; } = -1; // in GB, -1 means not available
        public float GpuTemperature { get; set; } = -1;
        public float GpuMaxTemperature { get; set; } = -1;
        public float Usage { get; set; } = -1; // in percentage, -1 means not available
    }

    public class MemoryInfo
    {
        public long TotalPhysicalMemory { get; set; } // in GB
        public long AvailablePhysicalMemory { get; set; } // in GB
        public long UsedPhysicalMemory { get; set; } // in GB
    }

    public class DiskInfo
    {
        public string DriveLetter { get; set; } = "";
        public long TotalSize { get; set; } // in GB
        public long AvailableSpace { get; set; } // in GB
        public long UsedSpace { get; set; } // in GB
    }
} 
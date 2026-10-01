using System;
using System.Diagnostics;
using System.Management;
using System.Threading;
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
        private const double BytesPerGb = 1024.0 * 1024.0 * 1024.0;
        private const long MinSampleIntervalMs = 1000;
        private const float MaxPlausibleTemperature = 125f;

        private readonly PerformanceCounter? _cpuCounter;
        private readonly Computer? _computer;
        private readonly ILogger<SystemMonitorService>? _logger;

        // PerformanceCounter, WMI and LibreHardwareMonitor are not thread-safe and several view models poll us.
        private readonly SemaphoreSlim _gate = new(1, 1);
        private SystemInfo? _lastInfo;
        private long _lastSampleTick;

        // Static GPU data from WMI (name / driver / adapter RAM) is queried once and cached.
        private bool _gpuStaticLoaded;
        private string _gpuName = string.Empty;
        private string _gpuDriverVersion = string.Empty;
        private long _gpuAdapterRamBytes = -1;

        private bool _disposed;

        public SystemMonitorService(ILogger<SystemMonitorService>? logger = null)
        {
            _logger = logger;

            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _cpuCounter.NextValue(); // prime: first call always returns 0
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize CPU performance counter; CPU usage will fall back to hardware sensors");
                try { _cpuCounter?.Dispose(); } catch { /* ignore */ }
                _cpuCounter = null;
            }

            try
            {
                var computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true
                };
                computer.Open();
                _computer = computer;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to open LibreHardwareMonitor; temperatures and GPU data will be unavailable");
                _computer = null;
            }

            _logger?.LogInformation("SystemMonitorService initialized (cpuCounter={Counter}, hardwareMonitor={Hw})",
                _cpuCounter != null, _computer != null);
        }

        public async Task<SystemInfo> GetSystemInfoAsync()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SystemMonitorService));

            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(SystemMonitorService));

                // Several view models poll at the same time; serve a recent sample instead of re-sampling.
                var cached = _lastInfo;
                if (cached != null && Environment.TickCount64 - _lastSampleTick < MinSampleIntervalMs)
                    return cached;

                var info = await Task.Run(CollectSystemInfo).ConfigureAwait(false);
                _lastInfo = info;
                _lastSampleTick = Environment.TickCount64;
                return info;
            }
            finally
            {
                _gate.Release();
            }
        }

        private SystemInfo CollectSystemInfo()
        {
            try
            {
                UpdateHardware();
                EnsureGpuStaticInfo();

                var info = new SystemInfo
                {
                    CpuUsage = GetCpuUsage(),
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
        }

        private void UpdateHardware()
        {
            if (_computer == null) return;
            foreach (var hardware in _computer.Hardware)
            {
                if (IsCpu(hardware) || IsGpu(hardware))
                {
                    try { hardware.Update(); }
                    catch (Exception ex) { _logger?.LogDebug(ex, "Hardware update failed for {Name}", hardware.Name); }
                }
            }
        }

        private static bool IsCpu(IHardware hardware) => hardware.HardwareType == HardwareType.Cpu;

        private static bool IsGpu(IHardware hardware) =>
            hardware.HardwareType == HardwareType.GpuNvidia
            || hardware.HardwareType == HardwareType.GpuAmd
            || hardware.HardwareType == HardwareType.GpuIntel;

        private float GetCpuUsage()
        {
            if (_cpuCounter != null)
            {
                try
                {
                    return _cpuCounter.NextValue();
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "CPU performance counter read failed; falling back to hardware sensor");
                }
            }

            // Fallback: "CPU Total" load sensor from LibreHardwareMonitor
            if (_computer != null)
            {
                foreach (var hardware in _computer.Hardware.Where(IsCpu))
                {
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("Total", StringComparison.OrdinalIgnoreCase)
                            && sensor.Value.HasValue && !float.IsNaN(sensor.Value.Value))
                        {
                            return sensor.Value.Value;
                        }
                    }
                }
            }
            return 0;
        }

        /// <summary>
        /// A temperature-typed sensor reading is only accepted when it is a plausible temperature.
        /// Rejects missing/NaN/zero/negative/absurd values and Intel's "Distance to TjMax" sensors.
        /// </summary>
        private static bool TryGetValidTemperature(ISensor sensor, out float value)
        {
            value = -1;
            if (sensor.SensorType != SensorType.Temperature || !sensor.Value.HasValue)
                return false;
            if (sensor.Name.Contains("Distance", StringComparison.OrdinalIgnoreCase))
                return false;

            float v = sensor.Value.Value;
            if (float.IsNaN(v) || float.IsInfinity(v) || v <= 0 || v > MaxPlausibleTemperature)
                return false;

            value = v;
            return true;
        }

        private float GetCpuTemperature(out float maxTemp)
        {
            float coreTemp = -1;
            maxTemp = -1;
            if (_computer == null) return -1;

            foreach (var hardware in _computer.Hardware)
            {
                if (!IsCpu(hardware)) continue;
                foreach (var sensor in hardware.Sensors)
                {
                    if (!TryGetValidTemperature(sensor, out float value)) continue;

                    // Track the hottest reading as max
                    if (value > maxTemp)
                        maxTemp = value;

                    // Prefer "CPU Package" or "Core (Tctl/Tdie)" as the primary reading
                    if (sensor.Name.Contains("Package") || sensor.Name.Contains("Tctl") || sensor.Name.Contains("Tdie"))
                    {
                        coreTemp = value;
                    }
                    // Fallback: use first temperature sensor if no specific one found yet
                    else if (coreTemp < 0)
                    {
                        coreTemp = value;
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
            if (_computer == null) return -1;

            foreach (var hardware in _computer.Hardware)
            {
                if (!IsGpu(hardware)) continue;
                foreach (var sensor in hardware.Sensors)
                {
                    if (!TryGetValidTemperature(sensor, out float value)) continue;

                    // Track the hottest reading as max (e.g. Hot Spot)
                    if (value > maxTemp)
                        maxTemp = value;

                    // Prefer "GPU Core" as the primary reading
                    if (sensor.Name.Contains("GPU Core"))
                    {
                        coreTemp = value;
                    }
                    // Fallback: use first temperature sensor if no specific one found yet
                    else if (coreTemp < 0)
                    {
                        coreTemp = value;
                    }
                }
            }
            if (coreTemp < 0) maxTemp = -1;
            return coreTemp;
        }

        /// <summary>Queries the static WMI video controller data (name, driver, adapter RAM) once and caches it.</summary>
        private void EnsureGpuStaticInfo()
        {
            if (_gpuStaticLoaded) return;
            _gpuStaticLoaded = true;

            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    using (obj)
                    {
                        _gpuName = obj["Name"]?.ToString() ?? string.Empty;
                        _gpuDriverVersion = obj["DriverVersion"]?.ToString() ?? string.Empty;
                        try
                        {
                            // AdapterRAM is a UInt32 in WMI (saturates at 4 GB), so do not test for ulong.
                            if (obj["AdapterRAM"] != null)
                            {
                                ulong ram = Convert.ToUInt64(obj["AdapterRAM"]);
                                if (ram > 0) _gpuAdapterRamBytes = (long)ram;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogDebug(ex, "Could not read AdapterRAM");
                        }
                    }
                    break; // Get first GPU only
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "WMI video controller query failed");
            }
        }

        private GpuInfo GetGpuInfo()
        {
            var gpuInfo = new GpuInfo
            {
                Name = _gpuName,
                DriverVersion = _gpuDriverVersion
            };

            double vramMb = -1;
            if (_computer != null)
            {
                foreach (var hardware in _computer.Hardware)
                {
                    if (!IsGpu(hardware)) continue;
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Load && sensor.Name.Contains("GPU Core")
                            && sensor.Value.HasValue && !float.IsNaN(sensor.Value.Value))
                        {
                            gpuInfo.Usage = sensor.Value.Value;
                        }
                        else if (sensor.SensorType == SensorType.SmallData
                                 && sensor.Name.Contains("GPU Memory Total", StringComparison.OrdinalIgnoreCase)
                                 && sensor.Value.HasValue && sensor.Value.Value > 0 && vramMb < 0)
                        {
                            vramMb = sensor.Value.Value; // MB
                        }
                    }
                }
            }

            // LibreHardwareMonitor reports the real size; WMI AdapterRAM is a UInt32 that caps at 4 GB,
            // so prefer the sensor and use WMI as the fallback.
            if (vramMb > 0)
                gpuInfo.VideoMemory = ToWholeGb(vramMb * 1024.0 * 1024.0);
            else if (_gpuAdapterRamBytes > 0)
                gpuInfo.VideoMemory = ToWholeGb(_gpuAdapterRamBytes);
            else
                gpuInfo.VideoMemory = -1;

            return gpuInfo;
        }

        private static long ToWholeGb(double bytes)
        {
            long gb = (long)Math.Round(bytes / BytesPerGb);
            return gb < 1 ? 1 : gb;
        }

        private MemoryInfo GetMemoryInfo()
        {
            var memoryInfo = new MemoryInfo();
            var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
            double total = computerInfo.TotalPhysicalMemory;
            double available = computerInfo.AvailablePhysicalMemory;
            memoryInfo.TotalPhysicalMemory = total / BytesPerGb;
            memoryInfo.AvailablePhysicalMemory = available / BytesPerGb;
            memoryInfo.UsedPhysicalMemory = (total - available) / BytesPerGb;
            return memoryInfo;
        }

        private List<DiskInfo> GetDiskInfo()
        {
            var disks = new List<DiskInfo>();
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
            {
                try
                {
                    long totalBytes = drive.TotalSize;
                    long freeBytes = drive.AvailableFreeSpace;
                    long usedBytes = totalBytes - freeBytes;
                    disks.Add(new DiskInfo
                    {
                        DriveLetter = drive.Name,
                        TotalSize = (long)(totalBytes / BytesPerGb),
                        AvailableSpace = (long)(freeBytes / BytesPerGb),
                        UsedSpace = (long)(usedBytes / BytesPerGb),
                        UsedPercent = totalBytes > 0 ? (double)usedBytes / totalBytes * 100.0 : 0
                    });
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "Could not read drive {Drive}", drive.Name);
                }
            }
            return disks;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                try
                {
                    // Let an in-flight sample finish before tearing down the shared resources.
                    bool locked = _gate.Wait(TimeSpan.FromSeconds(2));
                    try
                    {
                        _cpuCounter?.Dispose();
                        _computer?.Close();
                        _logger?.LogInformation("SystemMonitorService disposed successfully");
                    }
                    finally
                    {
                        if (locked) _gate.Release();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error disposing SystemMonitorService");
                }
            }
        }
    }

    public class SystemInfo
    {
        public float CpuUsage { get; set; }
        public float CpuTemperature { get; set; } = -1; // -1 means not available
        public float CpuMaxTemperature { get; set; } = -1;
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
        public double TotalPhysicalMemory { get; set; } // in GB
        public double AvailablePhysicalMemory { get; set; } // in GB
        public double UsedPhysicalMemory { get; set; } // in GB
    }

    public class DiskInfo
    {
        public string DriveLetter { get; set; } = "";
        public long TotalSize { get; set; } // in GB
        public long AvailableSpace { get; set; } // in GB
        public long UsedSpace { get; set; } // in GB
        public double UsedPercent { get; set; } // 0-100, computed from bytes
    }
}

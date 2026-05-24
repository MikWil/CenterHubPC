using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class SoundControlsViewModel : BaseViewModel
    {
        public ObservableCollection<DeviceViewModel> Devices { get; } = new();

        public SoundControlsViewModel(ILogger<SoundControlsViewModel>? logger = null) : base(logger)
        {
            try
            {
                var deviceEnum = new MMDeviceEnumerator();
                var devices = deviceEnum.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                foreach (var device in devices)
                {
                    Devices.Add(new DeviceViewModel(device, Logger));
                }
                Logger?.LogInformation("SoundControlsViewModel initialized with {Count} devices", Devices.Count);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to initialize SoundControlsViewModel");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                try
                {
                    foreach (var device in Devices)
                    {
                        device?.Dispose();
                    }
                    Devices.Clear();
                    Logger?.LogInformation("SoundControlsViewModel disposed");
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error disposing SoundControlsViewModel");
                }
            }
            base.Dispose(disposing);
        }
    }

    public partial class DeviceViewModel : BaseViewModel
    {
        private readonly MMDevice _device;
        public string DeviceName => _device.FriendlyName;
        public ObservableCollection<AudioSessionViewModel> Sessions { get; } = new();

        public DeviceViewModel(MMDevice device, ILogger? logger = null) : base(logger)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
            try
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    Sessions.Add(new AudioSessionViewModel(sessions[i], Logger));
                }
                Logger?.LogDebug("DeviceViewModel initialized for device: {DeviceName}", DeviceName);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to initialize DeviceViewModel for device: {DeviceName}", DeviceName);
            }
        }

        public float Volume
        {
            get => _device.AudioEndpointVolume.MasterVolumeLevelScalar;
            set
            {
                if (_device.AudioEndpointVolume.MasterVolumeLevelScalar != value)
                {
                    _device.AudioEndpointVolume.MasterVolumeLevelScalar = value;
                    OnPropertyChanged();
                    Logger?.LogDebug("Volume changed for device {DeviceName}: {Volume}", DeviceName, value);
                }
            }
        }

        public bool IsMuted
        {
            get => _device.AudioEndpointVolume.Mute;
            set
            {
                if (_device.AudioEndpointVolume.Mute != value)
                {
                    _device.AudioEndpointVolume.Mute = value;
                    OnPropertyChanged();
                    Logger?.LogDebug("Mute state changed for device {DeviceName}: {IsMuted}", DeviceName, value);
                }
            }
        }

        [RelayCommand]
        public void ToggleMute()
        {
            IsMuted = !IsMuted;
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                try
                {
                    foreach (var session in Sessions)
                    {
                        session?.Dispose();
                    }
                    Sessions.Clear();
                    _device?.Dispose();
                    Logger?.LogDebug("DeviceViewModel disposed for device: {DeviceName}", DeviceName);
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error disposing DeviceViewModel for device: {DeviceName}", DeviceName);
                }
            }
            base.Dispose(disposing);
        }
    }

    public partial class AudioSessionViewModel : BaseViewModel
    {
        private readonly AudioSessionControl _session;
        private readonly SimpleAudioVolume _volume;

        public string DisplayName
        {
            get
            {
                try
                {
                    int pid = (int)_session.GetProcessID;
                    if (pid != 0)
                    {
                        var proc = System.Diagnostics.Process.GetProcessById(pid);
                        if (!string.IsNullOrEmpty(proc.ProcessName))
                            return proc.ProcessName;
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Failed to get display name for audio session");
                }
                return "Unknown";
            }
        }

        private IImage? _appIcon;
        public IImage? AppIcon => _appIcon;

        public AudioSessionViewModel(AudioSessionControl session, ILogger? logger = null) : base(logger)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _volume = session.SimpleAudioVolume;
            _appIcon = GetAppIcon();
            Logger?.LogDebug("AudioSessionViewModel initialized for session: {DisplayName}", DisplayName);
        }

        private IImage? GetAppIcon()
        {
            try
            {
                int pid = (int)_session.GetProcessID;
                if (pid != 0)
                {
                    var proc = Process.GetProcessById(pid);
                    if (!string.IsNullOrEmpty(proc.MainModule?.FileName))
                    {
                        var icon = System.Drawing.Icon.ExtractAssociatedIcon(proc.MainModule.FileName);
                        if (icon != null)
                        {
                            using var bmp = icon.ToBitmap();
                            using var ms = new MemoryStream();
                            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                            ms.Position = 0;
                            return new Bitmap(ms);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Failed to get app icon for audio session: {DisplayName}", DisplayName);
            }
            return null;
        }

        public float Volume
        {
            get => _volume.Volume;
            set
            {
                if (_volume.Volume != value)
                {
                    _volume.Volume = value;
                    OnPropertyChanged();
                    Logger?.LogDebug("Volume changed for session {DisplayName}: {Volume}", DisplayName, value);
                }
            }
        }

        public bool IsMuted
        {
            get => _volume.Mute;
            set
            {
                if (_volume.Mute != value)
                {
                    _volume.Mute = value;
                    OnPropertyChanged();
                    Logger?.LogDebug("Mute state changed for session {DisplayName}: {IsMuted}", DisplayName, value);
                }
            }
        }

        [RelayCommand]
        public void ToggleMute()
        {
            IsMuted = !IsMuted;
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                try
                {
                    _session?.Dispose();
                    Logger?.LogDebug("AudioSessionViewModel disposed for session: {DisplayName}", DisplayName);
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error disposing AudioSessionViewModel for session: {DisplayName}", DisplayName);
                }
            }
            base.Dispose(disposing);
        }
    }
}
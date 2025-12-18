using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AudioSwitcher.AudioApi.CoreAudio;
using CenterHubNew.MVVM.Models;
using CenterHubNew.MVVM.Services;

namespace CenterHubNew.MVVM.ViewModel
{
    public partial class SoundViewModel : BaseViewModel
    {
        [ObservableProperty]
        private List<string> activeSoundSource = new() { "Loading" };

        [ObservableProperty]
        private string? selectedSoundSource;

        [ObservableProperty]
        private bool isMicrophoneMuted;

        [ObservableProperty]
        private float outputVolume;

        [ObservableProperty]
        private bool isOutputMuted;

        [ObservableProperty]
        private SoundProfile profile1 = new SoundProfile("Profile 1", null, null, 1.0f);

        [ObservableProperty]
        private SoundProfile profile2 = new SoundProfile("Profile 2", null, null, 1.0f);

        [ObservableProperty]
        private SoundProfile profile3 = new SoundProfile("Profile 3", null, null, 1.0f);

        [ObservableProperty]
        private int selectedProfileIndex = -1;

        public string MicrophoneMuteButtonText => IsMicrophoneMuted ? "Unmute Microphone" : "Mute Microphone";

        private readonly MMDeviceEnumerator deviceEnumerator;
        private List<MMDevice>? allSoundDevices;
        private bool _isUpdatingVolumeFromDevice = false;
        private readonly SoundProfileService _profileService;
        private bool _isLoadingProfiles = false;

        public SoundViewModel(ILogger<SoundViewModel>? logger = null) : base(logger)
        {
            deviceEnumerator = new MMDeviceEnumerator();
            // Create logger for SoundProfileService using logger factory if available
            ILogger<SoundProfileService>? profileServiceLogger = null;
            try
            {
                var loggerFactory = App.Services?.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
                profileServiceLogger = loggerFactory?.CreateLogger<SoundProfileService>();
            }
            catch
            {
                // Logger is optional, so null is acceptable
            }
            _profileService = new SoundProfileService(profileServiceLogger);
            LoadProfiles();
            Task.Run(LoadSoundDevices);
            Task.Run(LoadMicrophoneDevice);
            Task.Run(LoadOutputDevice);
            Logger?.LogInformation("SoundViewModel initialized");
        }

        private void LoadSoundDevices()
        {
            if (IsDisposed) return;

            // Run on UI thread to avoid COM threading issues
            App.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                    var allDevices = devices.ToList();
                    var deviceNames = allDevices.Select(x => x.FriendlyName).ToList();

                    allSoundDevices = allDevices;
                    ActiveSoundSource = deviceNames;
                    if (allSoundDevices.Count > 0)
                        SelectedSoundSource = allSoundDevices[0].FriendlyName;
                    
                    Logger?.LogInformation("Loaded {Count} sound devices", deviceNames.Count);
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error loading sound devices");
                    ActiveSoundSource = new List<string> { $"Error loading devices: {ex.Message}" };
                    allSoundDevices = null;
                }
            });
        }

        [RelayCommand(CanExecute = nameof(CanExecuteDeviceCommand))]
        private async Task SetDefaultPlaybackDeviceAsync()
        {
            if (IsDisposed || string.IsNullOrEmpty(SelectedSoundSource)) {
                Logger?.LogWarning("SetDefaultPlaybackDevice: Command cannot execute (disposed or no selection)");
                return;
            }

            try
            {
                var controller = new CoreAudioController();
                var device = (await controller.GetDevicesAsync(AudioSwitcher.AudioApi.DeviceType.Playback, AudioSwitcher.AudioApi.DeviceState.Active))
                    .FirstOrDefault(d => d.FullName == SelectedSoundSource);
                if (device != null)
                {
                    await device.SetAsDefaultAsync();
                    Logger?.LogInformation("Set {DeviceName} as default playback device", device.FullName);
                }
                else
                {
                    Logger?.LogWarning("Device not found for {DeviceName}", SelectedSoundSource);
                    ToastService.Instance.Error($"Device not found: {SelectedSoundSource}");
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to set default playback device");
                ToastService.Instance.Error($"Failed to set playback device: {ex.Message}");
            }
        }

        [RelayCommand(CanExecute = nameof(CanExecuteDeviceCommand))]
        private async Task SetDefaultCommunicationDeviceAsync()
        {
            if (IsDisposed || string.IsNullOrEmpty(SelectedSoundSource)) {
                Logger?.LogWarning("SetDefaultCommunicationDevice: Command cannot execute (disposed or no selection)");
                return;
            }

            try
            {
                var controller = new CoreAudioController();
                var devices = await controller.GetDevicesAsync(AudioSwitcher.AudioApi.DeviceType.Playback, AudioSwitcher.AudioApi.DeviceState.Active);
                var device = devices.FirstOrDefault(d => d.FullName == SelectedSoundSource)
                           ?? devices.FirstOrDefault(d => d.Name == SelectedSoundSource);

                if (device != null)
                {
                    await device.SetAsDefaultCommunicationsAsync();
                    Logger?.LogInformation("Set {DeviceName} as default communication device", device.FullName);
                    ToastService.Instance.Success("Communication device set");
                }
                else
                {
                    Logger?.LogWarning("Device not found for {DeviceName}", SelectedSoundSource);
                    ToastService.Instance.Error($"Device not found: {SelectedSoundSource}");
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to set default communication device");
                ToastService.Instance.Error($"Failed to set communication device: {ex.Message}");
            }
        }

        private void LoadMicrophoneDevice()
        {
            if (IsDisposed) return;

            // Run on UI thread to avoid COM threading issues
            App.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                    if (defaultDevice != null)
                    {
                        IsMicrophoneMuted = defaultDevice.AudioEndpointVolume.Mute;
                        Logger?.LogInformation("Loaded default microphone device: {DeviceName}, Muted: {IsMuted}", 
                            defaultDevice.FriendlyName, IsMicrophoneMuted);
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error loading microphone device");
                }
            });
        }

        private void LoadOutputDevice()
        {
            if (IsDisposed) return;

            // Run on UI thread to avoid COM threading issues
            App.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (defaultDevice != null)
                    {
                        _isUpdatingVolumeFromDevice = true;
                        OutputVolume = defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar;
                        IsOutputMuted = defaultDevice.AudioEndpointVolume.Mute;
                        _isUpdatingVolumeFromDevice = false;
                        Logger?.LogInformation("Loaded default output device: {DeviceName}, Volume: {Volume}, Muted: {IsMuted}", 
                            defaultDevice.FriendlyName, OutputVolume, IsOutputMuted);
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error loading output device");
                }
            });
        }

        [RelayCommand]
        private void ToggleMicrophoneMute()
        {
            if (IsDisposed)
            {
                Logger?.LogWarning("ToggleMicrophoneMute: Cannot execute (disposed)");
                return;
            }

            try
            {
                // Get a fresh reference to the default microphone device each time
                using var defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                if (defaultDevice != null)
                {
                    IsMicrophoneMuted = !IsMicrophoneMuted;
                    defaultDevice.AudioEndpointVolume.Mute = IsMicrophoneMuted;
                    Logger?.LogInformation("Microphone mute toggled: {IsMuted}", IsMicrophoneMuted);
                }
                else
                {
                    Logger?.LogWarning("ToggleMicrophoneMute: No default microphone device found");
                    ToastService.Instance.Warning("No default microphone found");
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to toggle microphone mute");
                ToastService.Instance.Error($"Failed to toggle microphone: {ex.Message}");
            }
        }

        partial void OnOutputVolumeChanged(float value)
        {
            if (IsDisposed || _isUpdatingVolumeFromDevice) return;

            try
            {
                using var defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (defaultDevice != null)
                {
                    defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar = value;
                    Logger?.LogDebug("Output volume changed to: {Volume}", value);
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to set output volume");
            }
        }

        partial void OnIsOutputMutedChanged(bool value)
        {
            if (IsDisposed || _isUpdatingVolumeFromDevice) return;

            try
            {
                using var defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (defaultDevice != null)
                {
                    defaultDevice.AudioEndpointVolume.Mute = value;
                    Logger?.LogDebug("Output mute state changed to: {IsMuted}", value);
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to set output mute state");
            }
        }

        private bool CanExecuteDeviceCommand() => 
            !IsDisposed && 
            !string.IsNullOrEmpty(SelectedSoundSource);

        partial void OnIsMicrophoneMutedChanged(bool value)
        {
            OnPropertyChanged(nameof(MicrophoneMuteButtonText));
            Logger?.LogDebug("Microphone mute state changed to: {IsMuted}", value);
        }

        [RelayCommand(CanExecute = nameof(CanExecuteDeviceCommand))]
        private async Task SetActiveOutputDeviceAsync()
        {
            if (IsDisposed || string.IsNullOrEmpty(SelectedSoundSource))
            {
                Logger?.LogWarning("SetActiveOutputDevice: Command cannot execute (disposed or no selection)");
                return;
            }

            try
            {
                var controller = new CoreAudioController();
                var device = (await controller.GetDevicesAsync(AudioSwitcher.AudioApi.DeviceType.Playback, AudioSwitcher.AudioApi.DeviceState.Active))
                    .FirstOrDefault(d => d.FullName == SelectedSoundSource);
                if (device != null)
                {
                    await device.SetAsDefaultAsync();
                    // Reload output device to update volume/mute state
                    Task.Run(LoadOutputDevice);
                    Logger?.LogInformation("Set {DeviceName} as active output device", device.FullName);
                }
                else
                {
                    Logger?.LogWarning("Device not found for {DeviceName}", SelectedSoundSource);
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to set active output device");
            }
        }

        partial void OnSelectedSoundSourceChanged(string? value)
        {
            SetDefaultPlaybackDeviceCommand.NotifyCanExecuteChanged();
            SetDefaultCommunicationDeviceCommand.NotifyCanExecuteChanged();
            SetActiveOutputDeviceCommand.NotifyCanExecuteChanged();
            Logger?.LogDebug("Selected sound source changed to: {DeviceName}", value);
        }

        private void LoadProfiles()
        {
            try
            {
                _isLoadingProfiles = true;
                var profiles = _profileService.LoadProfiles();
                if (profiles.Count >= 3)
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        Profile1 = profiles[0];
                        Profile2 = profiles[1];
                        Profile3 = profiles[2];
                    });
                    Logger?.LogInformation("Loaded sound profiles");
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error loading sound profiles");
            }
            finally
            {
                _isLoadingProfiles = false;
            }
        }

        [RelayCommand]
        private void SaveCurrentProfile(object? parameter)
        {
            if (!int.TryParse(parameter?.ToString(), out int profileIndex))
            {
                Logger?.LogWarning("SaveCurrentProfile: Invalid parameter {Parameter}", parameter);
                return;
            }

            if (IsDisposed || profileIndex < 0 || profileIndex > 2)
            {
                Logger?.LogWarning("SaveCurrentProfile: Invalid profile index {Index}", profileIndex);
                return;
            }

            try
            {
                // Get current default devices - must be done on UI thread for COM
                string? currentAudioDevice = null;
                string? currentCommDevice = null;

                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    using var defaultAudioDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (defaultAudioDevice != null)
                    {
                        currentAudioDevice = defaultAudioDevice.FriendlyName;
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Could not get current audio device");
                }

                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    using var defaultCommDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Communications);
                    if (defaultCommDevice != null)
                    {
                        currentCommDevice = defaultCommDevice.FriendlyName;
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Could not get current communication device");
                }

                var profile = new SoundProfile
                {
                    Name = profileIndex == 0 ? Profile1.Name : profileIndex == 1 ? Profile2.Name : Profile3.Name,
                    CommunicationDevice = currentCommDevice,
                    AudioDevice = currentAudioDevice,
                    Volume = OutputVolume
                };

                if (profileIndex == 0)
                {
                    Profile1 = profile;
                    _profileService.SaveProfile(0, profile);
                }
                else if (profileIndex == 1)
                {
                    Profile2 = profile;
                    _profileService.SaveProfile(1, profile);
                }
                else
                {
                    Profile3 = profile;
                    _profileService.SaveProfile(2, profile);
                }

                Logger?.LogInformation("Saved profile {Index}: {Name} (Audio: {Audio}, Comm: {Comm}, Volume: {Volume})", 
                    profileIndex, profile.Name, profile.AudioDevice, profile.CommunicationDevice, profile.Volume);
                ToastService.Instance.Success($"Profile {profileIndex + 1} saved");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error saving profile {Index}", profileIndex);
                ToastService.Instance.Error($"Failed to save profile: {ex.Message}");
            }
        }

        [RelayCommand]
        private async Task ApplyProfileAsync(object? parameter)
        {
            if (!int.TryParse(parameter?.ToString(), out int profileIndex))
            {
                Logger?.LogWarning("ApplyProfile: Invalid parameter {Parameter}", parameter);
                return;
            }

            if (IsDisposed || profileIndex < 0 || profileIndex > 2)
            {
                Logger?.LogWarning("ApplyProfile: Invalid profile index {Index}", profileIndex);
                return;
            }

            try
            {
                SoundProfile profile = profileIndex == 0 ? Profile1 : profileIndex == 1 ? Profile2 : Profile3;

                if (string.IsNullOrEmpty(profile.AudioDevice) && string.IsNullOrEmpty(profile.CommunicationDevice))
                {
                    ToastService.Instance.Warning("This profile has no devices configured");
                    return;
                }

                var controller = new CoreAudioController();
                var devices = await controller.GetDevicesAsync(AudioSwitcher.AudioApi.DeviceType.Playback, AudioSwitcher.AudioApi.DeviceState.Active);

                // Apply audio device
                if (!string.IsNullOrEmpty(profile.AudioDevice))
                {
                    var audioDevice = devices.FirstOrDefault(d => d.FullName == profile.AudioDevice)
                                   ?? devices.FirstOrDefault(d => d.Name == profile.AudioDevice);
                    if (audioDevice != null)
                    {
                        await audioDevice.SetAsDefaultAsync();
                        Logger?.LogInformation("Applied audio device: {DeviceName}", audioDevice.FullName);
                    }
                }

                // Apply communication device
                if (!string.IsNullOrEmpty(profile.CommunicationDevice))
                {
                    var commDevice = devices.FirstOrDefault(d => d.FullName == profile.CommunicationDevice)
                                  ?? devices.FirstOrDefault(d => d.Name == profile.CommunicationDevice);
                    if (commDevice != null)
                    {
                        await commDevice.SetAsDefaultCommunicationsAsync();
                        Logger?.LogInformation("Applied communication device: {DeviceName}", commDevice.FullName);
                    }
                }

                // Apply volume
                if (profile.Volume >= 0 && profile.Volume <= 1)
                {
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        _isUpdatingVolumeFromDevice = true;
                        OutputVolume = profile.Volume;
                        _isUpdatingVolumeFromDevice = false;
                    });

                    // Set volume on the current default device
                    using var defaultDevice = deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (defaultDevice != null)
                    {
                        defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar = profile.Volume;
                    }
                }

                // Reload devices to update UI
                Task.Run(LoadOutputDevice);
                Task.Run(LoadSoundDevices);

                SelectedProfileIndex = profileIndex;
                Logger?.LogInformation("Applied profile {Index}: {Name}", profileIndex, profile.Name);
                ToastService.Instance.Success($"Profile '{profile.Name}' applied");
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Error applying profile {Index}", profileIndex);
                ToastService.Instance.Error($"Failed to apply profile: {ex.Message}");
            }
        }

        partial void OnProfile1Changed(SoundProfile value)
        {
            if (value != null && !IsDisposed && !_isLoadingProfiles)
            {
                try
                {
                    _profileService.SaveProfile(0, value);
                    Logger?.LogDebug("Auto-saved profile 1: {Name}", value.Name);
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error auto-saving profile 1");
                }
            }
        }

        partial void OnProfile2Changed(SoundProfile value)
        {
            if (value != null && !IsDisposed && !_isLoadingProfiles)
            {
                try
                {
                    _profileService.SaveProfile(1, value);
                    Logger?.LogDebug("Auto-saved profile 2: {Name}", value.Name);
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error auto-saving profile 2");
                }
            }
        }

        partial void OnProfile3Changed(SoundProfile value)
        {
            if (value != null && !IsDisposed && !_isLoadingProfiles)
            {
                try
                {
                    _profileService.SaveProfile(2, value);
                    Logger?.LogDebug("Auto-saved profile 3: {Name}", value.Name);
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error auto-saving profile 3");
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed && disposing)
            {
                try
                {
                    deviceEnumerator?.Dispose();
                    if (allSoundDevices != null)
                    {
                        foreach (var device in allSoundDevices)
                        {
                            device?.Dispose();
                        }
                    }
                    Logger?.LogInformation("SoundViewModel disposed");
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "Error disposing SoundViewModel");
                }
            }
            base.Dispose(disposing);
        }
    }
}

using System.Collections.Generic;
using System.Threading.Tasks;
using CenterHubNew.MVVM.Models;

namespace CenterHubNew.MVVM.Services
{
    /// <summary>A Windows audio endpoint for list/selection purposes.</summary>
    public sealed class AudioDeviceInfo
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public bool IsCapture { get; init; }
        public override string ToString() => Name;
    }

    /// <summary>
    /// Central Windows default-device service. Reading is done with NAudio
    /// (stable endpoint IDs), switching with AudioSwitcher's CoreAudioController.
    /// Matching prefers endpoint ID, then full/friendly name — so a saved config
    /// keeps working after a display-name change, and a snapshot restores exactly.
    ///
    /// This consolidates what previously lived inline in SoundViewModel and adds
    /// recording-device switching + ID matching, which Voicemeeter mode needs.
    /// </summary>
    public interface IAudioDeviceService
    {
        IReadOnlyList<AudioDeviceInfo> GetPlaybackDevices();
        IReadOnlyList<AudioDeviceInfo> GetRecordingDevices();

        AudioEndpointRef? GetDefaultPlayback();
        AudioEndpointRef? GetDefaultCommunicationsPlayback();
        AudioEndpointRef? GetDefaultRecording();
        AudioEndpointRef? GetDefaultCommunicationsRecording();

        /// <summary>Capture the four current default endpoints (by ID + name).</summary>
        AudioDeviceSnapshot CaptureSnapshot();

        Task<bool> SetDefaultPlaybackAsync(AudioEndpointRef device, bool communicationsToo);
        Task<bool> SetDefaultRecordingAsync(AudioEndpointRef device, bool communicationsToo);

        /// <summary>Set default playback to the first render device whose name contains <paramref name="nameSubstring"/>.</summary>
        Task<bool> SetDefaultPlaybackByNameAsync(string nameSubstring, bool communicationsToo);

        /// <summary>Set default recording to the first capture device whose name contains <paramref name="nameSubstring"/>.</summary>
        Task<bool> SetDefaultRecordingByNameAsync(string nameSubstring, bool communicationsToo);

        /// <summary>
        /// Restore the four defaults from a snapshot. Endpoints that can no longer be
        /// resolved are skipped (never restores a device that no longer exists).
        /// </summary>
        Task RestoreSnapshotAsync(AudioDeviceSnapshot snapshot);
    }
}

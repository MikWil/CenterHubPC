namespace CenterHubNew.MVVM.Models
{
    public enum AudioCheckStatus { Ok, Warning, Error }

    /// <summary>An actionable fix a check can offer, resolved to a command by the view-model.</summary>
    public enum AudioCheckFix
    {
        None,
        InstallVoicemeeter,
        StartBanana,
        RestartVoicemeeter,
        ReapplyRouting,
        ConfigureDevices,
        InstallVbCable
    }

    /// <summary>One line in the audio setup health-check, with status and an optional fix.</summary>
    public sealed class AudioCheck
    {
        public string Title { get; init; } = "";
        public AudioCheckStatus Status { get; init; }
        public string Detail { get; init; } = "";
        public AudioCheckFix Fix { get; init; } = AudioCheckFix.None;
        public string? FixLabel { get; init; }

        public bool HasFix => Fix != AudioCheckFix.None && !string.IsNullOrEmpty(FixLabel);
        public bool IsProblem => Status != AudioCheckStatus.Ok;

        /// <summary>Status dot colour for the UI (green / amber / red).</summary>
        public string StatusColor => Status switch
        {
            AudioCheckStatus.Ok => "#1D9E75",
            AudioCheckStatus.Warning => "#E0A030",
            _ => "#E24B4A"
        };
    }
}

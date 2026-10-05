namespace CenterHubNew.MVVM.Models
{
    /// <summary>Where the guitar looper is. The loop is slaved to the drum machine's bars.</summary>
    public enum LooperState
    {
        /// <summary>Nothing recorded.</summary>
        Empty,
        /// <summary>Record was pressed; the first take starts at the next bar.</summary>
        Armed,
        /// <summary>Recording the first take (it sets the loop length).</summary>
        Recording,
        /// <summary>The loop is playing (or waiting for the drums to start / the next bar).</summary>
        Playing,
        /// <summary>The loop plays and a new layer is being recorded on top of it.</summary>
        Overdubbing,
        /// <summary>The loop is silent but kept.</summary>
        Stopped,
    }
}

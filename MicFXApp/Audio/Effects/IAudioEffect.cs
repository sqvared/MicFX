using System.Collections.Generic;
using System.ComponentModel;

namespace MicFXApp.Audio.Effects
{
    /// <summary>
    /// Contract for a single node in the microphone effect chain.
    /// Every effect processes an interleaved 32-bit float buffer in place.
    /// </summary>
    public interface IAudioEffect : INotifyPropertyChanged
    {
        string DisplayName { get; }
        bool Enabled { get; set; }

        /// <summary>Short monogram shown in the chain UI's module badge, e.g. "G", "EQ", "CMP".</summary>
        string BadgeText { get; }

        /// <summary>Accent color (hex) this module type is represented by in the chain UI.</summary>
        string AccentHex { get; }

        /// <summary>
        /// Process the buffer in place.
        /// </summary>
        /// <param name="buffer">Interleaved float samples.</param>
        /// <param name="channels">Number of interleaved channels (1 or 2).</param>
        /// <param name="sampleRate">Sample rate in Hz.</param>
        void Process(float[] buffer, int channels, int sampleRate);

        /// <summary>Reset internal filter/envelope state (e.g. on stream restart).</summary>
        void Reset();

        /// <summary>Serializable snapshot of this effect's tunable knob values, for presets.</summary>
        Dictionary<string, double> GetParameters();

        /// <summary>Restore knob values from a preset. Unknown/missing keys are ignored.</summary>
        void SetParameters(Dictionary<string, double> parameters);
    }
}

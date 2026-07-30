namespace MicFXApp.Audio.Effects
{
    /// <summary>
    /// Implemented by effects that need a second audio stream alongside the mic
    /// signal - currently just AEC, which needs to know what's playing out of
    /// the speakers to subtract it back out of the mic signal. AudioEngine
    /// checks for this interface and feeds it a loopback capture of system
    /// playback before running the chain each callback.
    /// </summary>
    public interface IReferenceFedEffect
    {
        /// <summary>
        /// Supplies the most recent reference (loopback) samples, interleaved,
        /// in the same format the reference capture device produced them.
        /// May be called with a format that differs from the mic's own
        /// channels/sampleRate - implementations should handle that gracefully
        /// (e.g. by no-op'ing) rather than throwing.
        /// </summary>
        void SupplyReference(float[] referenceSamples, int channels, int sampleRate);
    }
}

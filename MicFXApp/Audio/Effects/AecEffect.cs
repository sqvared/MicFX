using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicFXApp.Audio.Effects
{
    /// <summary>
    /// Acoustic Echo Cancellation: an NLMS (Normalized Least Mean Squares)
    /// adaptive filter that learns the acoustic path from your speakers back
    /// into your mic, predicts the resulting echo from a reference tap of
    /// what's actually playing out of your speakers, and subtracts it.
    ///
    /// This is genuinely the hardest stage in the chain to get right, even for
    /// dedicated hardware/software - and it only matters if your mic can pick
    /// up your own speaker output (e.g. laptop mic + laptop speakers, no
    /// headphones). If you're on headphones, leave this disabled; it has
    /// nothing to cancel and is just spending CPU for no benefit.
    ///
    /// Scoped, honest limitations of this implementation:
    ///  - Only adapts when the reference (loopback) stream's sample rate
    ///    matches the mic's; otherwise it passes audio through unchanged
    ///    rather than attempting to resample.
    ///  - The adaptive filter is 512 taps (~10.6ms at 48kHz), so it can only
    ///    model echo paths shorter than that - fine for direct laptop
    ///    speaker-to-mic coupling, not for cancelling echo off room
    ///    reflections arriving later than that.
    ///  - Reference/mic alignment is a simple fixed-lag assumption
    ///    (re-derived fresh every callback from the reference ring buffer's
    ///    current write position, so it self-corrects rather than drifting),
    ///    not a measured/estimated delay - real AEC implementations spend
    ///    substantial effort on delay estimation that's out of scope here.
    /// </summary>
    public class AecEffect : IAudioEffect, IReferenceFedEffect
    {
        public string DisplayName => "Echo Cancellation (AEC)";
        public string BadgeText => "AEC";
        public string AccentHex => "#6C7BFF";

        // Off by default - most setups (headphones, or mic far from speakers) don't need this at all.
        private bool _enabled;
        public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }

        private double _strength = 50;
        /// <summary>Adaptation strength, 0-100%. Higher adapts faster but risks instability/artifacts.</summary>
        public double Strength
        {
            get => _strength;
            set { _strength = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        private const int FilterLength = 512;
        private const int RingSize = 1 << 16; // 65536, power of two
        private const int FixedLagSamples = 4096; // ~85ms headroom at 48kHz between reference write and read

        private readonly float[] _refRing = new float[RingSize];
        private int _refWritePos;
        private int _refSamplesWritten; // capped at RingSize, used to know when we have enough lead-in buffered
        private int _referenceSampleRate = -1;

        private float[][] _weights = Array.Empty<float[]>();
        private int _lastChannels = -1;

        public void SupplyReference(float[] referenceSamples, int refChannels, int refSampleRate)
        {
            if (refChannels < 1) refChannels = 1;
            _referenceSampleRate = refSampleRate;
            int frames = referenceSamples.Length / refChannels;

            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < refChannels; c++) sum += referenceSamples[f * refChannels + c];
                _refRing[_refWritePos] = sum / refChannels;
                _refWritePos = (_refWritePos + 1) & (RingSize - 1);
                if (_refSamplesWritten < RingSize) _refSamplesWritten++;
            }
        }

        public void Process(float[] buffer, int channels, int sampleRate)
        {
            if (channels < 1) channels = 1;
            if (_referenceSampleRate <= 0 || _referenceSampleRate != sampleRate) return; // no usable reference yet, or format mismatch - pass through untouched
            if (_refSamplesWritten < FilterLength + FixedLagSamples) return; // not enough lead-in buffered yet

            if (_weights.Length != channels)
            {
                _weights = new float[channels][];
                for (int c = 0; c < channels; c++) _weights[c] = new float[FilterLength];
                _lastChannels = channels;
            }

            int frames = buffer.Length / channels;

            // Snapshot the reference window this callback needs, re-derived fresh from the current
            // ring write position every time (rather than an incrementally-tracked read pointer) so any
            // relative drift between the two independently-clocked audio streams self-corrects each
            // callback instead of accumulating.
            int snapshotLen = FilterLength + frames;
            var refSnapshot = new float[snapshotLen];
            int baseRingPos = (_refWritePos - FixedLagSamples - frames + RingSize * 4) & (RingSize - 1);
            for (int i = 0; i < snapshotLen; i++)
                refSnapshot[i] = _refRing[(baseRingPos + i) & (RingSize - 1)];

            double mu = 0.02 + (_strength / 100.0) * 0.6; // conservative .. aggressive adaptation rate
            const double epsilon = 1e-6;

            for (int f = 0; f < frames; f++)
            {
                // Tap window for this output sample: refSnapshot[FilterLength-1+f-k] for k = 0..FilterLength-1
                int windowEnd = FilterLength - 1 + f;

                double energy = 0;
                for (int k = 0; k < FilterLength; k++)
                {
                    double x = refSnapshot[windowEnd - k];
                    energy += x * x;
                }
                double muEff = mu / (energy + epsilon);

                for (int c = 0; c < channels; c++)
                {
                    var w = _weights[c];
                    double yHat = 0;
                    for (int k = 0; k < FilterLength; k++)
                        yHat += w[k] * refSnapshot[windowEnd - k];

                    double mic = buffer[f * channels + c];
                    double error = mic - yHat;

                    for (int k = 0; k < FilterLength; k++)
                        w[k] += (float)(muEff * error * refSnapshot[windowEnd - k]);

                    float outVal = (float)error;
                    if (outVal > 1f) outVal = 1f; else if (outVal < -1f) outVal = -1f;
                    buffer[f * channels + c] = outVal;
                }
            }
        }

        public void Reset()
        {
            foreach (var w in _weights) Array.Clear(w, 0, w.Length);
        }

        public Dictionary<string, double> GetParameters() => new() { ["Strength"] = Strength };
        public void SetParameters(Dictionary<string, double> parameters)
        {
            if (parameters.TryGetValue("Strength", out var v)) Strength = v;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

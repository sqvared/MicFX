using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NAudio.Dsp;

namespace MicFXApp.Audio.Effects
{
    /// <summary>
    /// Adaptive noise suppressor using short-time spectral subtraction (a
    /// classic, non-machine-learning technique - see Boll 1979): it runs a
    /// windowed FFT over short overlapping frames, continuously tracks a
    /// per-frequency-bin noise floor estimate via a slow minimum-follower
    /// (fast to track down when the signal gets quieter, slow to rise so a
    /// sustained voice doesn't get mistaken for noise), then subtracts a
    /// multiple of that estimate from each frame's magnitude spectrum before
    /// reconstructing via inverse FFT and overlap-add.
    ///
    /// This is not the same class of tool as deep-learning suppressors like
    /// RNNoise or DeepFilterNet - those require bundling a trained neural
    /// network and an inference runtime. This is the classical DSP approach:
    /// lighter weight, no external model, but less aggressive on non-stationary
    /// noise (e.g. keyboard clatter) than a trained model would be.
    ///
    /// Introduces roughly one analysis window of latency (~21ms at 48kHz with
    /// the 1024-sample window below) - normal for STFT processing.
    /// </summary>
    public class NoiseSuppressorEffect : IAudioEffect
    {
        public string DisplayName => "Noise Suppressor";
        public string BadgeText => "NS";
        public string AccentHex => "#FF5C9E";

        private bool _enabled = true;
        public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }

        private double _amount = 60;
        /// <summary>Suppression strength, 0-100%. Higher removes more noise but risks "musical noise" artifacts.</summary>
        public double Amount
        {
            get => _amount;
            set { _amount = Math.Clamp(value, 0, 100); OnPropertyChanged(); }
        }

        private const int FrameSize = 1024;
        private const int Log2FrameSize = 10; // log2(1024)
        private const int HopSize = FrameSize / 2; // 50% overlap

        private static readonly double[] Window = BuildHannWindow();
        private static double[] BuildHannWindow()
        {
            var w = new double[FrameSize];
            for (int n = 0; n < FrameSize; n++) w[n] = FastFourierTransform.HannWindow(n, FrameSize);
            return w;
        }

        private class ChannelState
        {
            public readonly List<float> InputFifo = new();
            public readonly Queue<float> OutputFifo = new();
            public readonly float[] OverlapAdd = new float[FrameSize];
            public readonly float[] NoiseMag = new float[FrameSize / 2 + 1];
            public bool NoiseInitialized;
        }

        private ChannelState[] _channels = Array.Empty<ChannelState>();

        private void EnsureChannels(int channels)
        {
            if (_channels.Length == channels) return;
            _channels = new ChannelState[channels];
            for (int c = 0; c < channels; c++) _channels[c] = new ChannelState();
        }

        public void Process(float[] buffer, int channels, int sampleRate)
        {
            if (channels < 1) channels = 1;
            EnsureChannels(channels);
            int frames = buffer.Length / channels;

            for (int c = 0; c < channels; c++)
            {
                var st = _channels[c];
                for (int f = 0; f < frames; f++)
                    st.InputFifo.Add(buffer[f * channels + c]);

                while (st.InputFifo.Count >= FrameSize)
                {
                    ProcessFrame(st, sampleRate);
                    st.InputFifo.RemoveRange(0, HopSize); // slide forward one hop, keep the overlap tail
                }
            }

            for (int f = 0; f < frames; f++)
                for (int c = 0; c < channels; c++)
                {
                    var st = _channels[c];
                    buffer[f * channels + c] = st.OutputFifo.Count > 0 ? st.OutputFifo.Dequeue() : 0f;
                }
        }

        private readonly Complex[] _fftScratch = new Complex[FrameSize];

        private void ProcessFrame(ChannelState st, int sampleRate)
        {
            for (int n = 0; n < FrameSize; n++)
            {
                _fftScratch[n].X = (float)(st.InputFifo[n] * Window[n]);
                _fftScratch[n].Y = 0;
            }
            FastFourierTransform.FFT(true, Log2FrameSize, _fftScratch);

            int bins = FrameSize / 2 + 1; // 0..Nyquist inclusive
            double oversubtract = 1.0 + (_amount / 100.0) * 3.5; // ~1x (off) .. ~4.5x (aggressive)
            double floorFactor = 0.15 - (_amount / 100.0) * 0.13; // higher amount -> lower spectral floor

            // Noise-floor tracking time constants, converted to a per-frame smoothing coefficient
            // based on the actual hop duration. Deliberately asymmetric and NOT instant on the way
            // down: natural gaps between syllables in ordinary speech are often only 100-300ms, so
            // an instant-snap-to-any-dip tracker (the original bug here) ends up chasing the voice
            // signal itself down as "noise" within a few seconds of talking, over-suppressing it.
            // A ~0.4s fall time means only genuinely sustained quiet (not just a breath between
            // words) pulls the floor down; a ~4s rise time means real room noise gets learned
            // without a loud transient briefly poisoning the estimate.
            double hopSeconds = HopSize / (double)sampleRate;
            double fallCoeff = Math.Exp(-hopSeconds / 0.4);
            double riseCoeff = Math.Exp(-hopSeconds / 4.0);

            for (int k = 0; k < bins; k++)
            {
                double re = _fftScratch[k].X, im = _fftScratch[k].Y;
                double mag = Math.Sqrt(re * re + im * im);

                if (!st.NoiseInitialized)
                    st.NoiseMag[k] = (float)mag;
                else if (mag < st.NoiseMag[k])
                    st.NoiseMag[k] = (float)(fallCoeff * st.NoiseMag[k] + (1 - fallCoeff) * mag);
                else
                    st.NoiseMag[k] = (float)(riseCoeff * st.NoiseMag[k] + (1 - riseCoeff) * mag);

                double noise = st.NoiseMag[k];
                double gain = mag > 1e-9 ? 1.0 - oversubtract * (noise / mag) : 1.0;
                gain = Math.Clamp(gain, floorFactor, 1.0);

                _fftScratch[k].X = (float)(re * gain);
                _fftScratch[k].Y = (float)(im * gain);
            }
            st.NoiseInitialized = true;

            // Rebuild the conjugate-symmetric upper half so the inverse FFT produces a real-valued signal.
            for (int k = 1; k < bins - 1; k++)
            {
                int mirror = FrameSize - k;
                _fftScratch[mirror].X = _fftScratch[k].X;
                _fftScratch[mirror].Y = -_fftScratch[k].Y;
            }

            FastFourierTransform.FFT(false, Log2FrameSize, _fftScratch);

            // Overlap-add synthesis. NAudio's inverse FFT already divides by N internally (unlike the
            // forward transform), so no extra 1/N division belongs here - an earlier version of this
            // code divided by FrameSize again on top of that, which is what caused near-total silence
            // (an extra ~60dB of attenuation) whenever this effect was enabled.
            for (int n = 0; n < FrameSize; n++)
                st.OverlapAdd[n] += _fftScratch[n].X * (float)Window[n];

            for (int n = 0; n < HopSize; n++)
                st.OutputFifo.Enqueue(st.OverlapAdd[n]);

            Array.Copy(st.OverlapAdd, HopSize, st.OverlapAdd, 0, FrameSize - HopSize);
            Array.Clear(st.OverlapAdd, FrameSize - HopSize, HopSize);
        }

        public void Reset()
        {
            foreach (var st in _channels)
            {
                st.InputFifo.Clear();
                st.OutputFifo.Clear();
                Array.Clear(st.OverlapAdd, 0, st.OverlapAdd.Length);
                Array.Clear(st.NoiseMag, 0, st.NoiseMag.Length);
                st.NoiseInitialized = false;
            }
        }

        public Dictionary<string, double> GetParameters() => new() { ["Amount"] = Amount };
        public void SetParameters(Dictionary<string, double> parameters)
        {
            if (parameters.TryGetValue("Amount", out var v)) Amount = v;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

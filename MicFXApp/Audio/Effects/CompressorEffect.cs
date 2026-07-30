using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicFXApp.Audio.Effects
{
    /// <summary>
    /// Classic feed-forward peak compressor with linked (mono-summed) detection,
    /// so stereo signals don't shift image when gain reduction kicks in.
    /// </summary>
    public class CompressorEffect : IAudioEffect
    {
        public string DisplayName => "Compressor";
        public string BadgeText => "CMP";
        public string AccentHex => "#FF9166";

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        private double _thresholdDb = -18;
        public double ThresholdDb
        {
            get => _thresholdDb;
            set { _thresholdDb = Math.Clamp(value, -60, 0); OnPropertyChanged(); }
        }

        private double _ratio = 3.0;
        /// <summary>Compression ratio, e.g. 3 means 3:1.</summary>
        public double Ratio
        {
            get => _ratio;
            set { _ratio = Math.Clamp(value, 1, 20); OnPropertyChanged(); }
        }

        private double _attackMs = 10;
        public double AttackMs
        {
            get => _attackMs;
            set { _attackMs = Math.Clamp(value, 0.1, 200); OnPropertyChanged(); }
        }

        private double _releaseMs = 120;
        public double ReleaseMs
        {
            get => _releaseMs;
            set { _releaseMs = Math.Clamp(value, 5, 1000); OnPropertyChanged(); }
        }

        private double _makeupDb = 0;
        public double MakeupDb
        {
            get => _makeupDb;
            set { _makeupDb = Math.Clamp(value, 0, 24); OnPropertyChanged(); }
        }

        /// <summary>Current gain reduction in dB, exposed for metering.</summary>
        public double CurrentGainReductionDb { get; private set; }

        private double _envelope; // linear, smoothed detector level

        public void Process(float[] buffer, int channels, int sampleRate)
        {
            if (channels < 1) channels = 1;
            int frames = buffer.Length / channels;

            double attackCoeff = Math.Exp(-1.0 / (sampleRate * (_attackMs / 1000.0)));
            double releaseCoeff = Math.Exp(-1.0 / (sampleRate * (_releaseMs / 1000.0)));
            double makeupLinear = Math.Pow(10, _makeupDb / 20.0);

            for (int f = 0; f < frames; f++)
            {
                int baseIdx = f * channels;

                // detector: peak across channels for this frame
                double peak = 0;
                for (int c = 0; c < channels; c++)
                {
                    double a = Math.Abs(buffer[baseIdx + c]);
                    if (a > peak) peak = a;
                }

                // envelope follower (attack when rising, release when falling)
                if (peak > _envelope)
                    _envelope = attackCoeff * _envelope + (1 - attackCoeff) * peak;
                else
                    _envelope = releaseCoeff * _envelope + (1 - releaseCoeff) * peak;

                double levelDb = 20 * Math.Log10(Math.Max(_envelope, 1e-9));
                double gainReductionDb = 0;
                if (levelDb > _thresholdDb)
                {
                    double overDb = levelDb - _thresholdDb;
                    gainReductionDb = overDb - overDb / _ratio;
                }

                CurrentGainReductionDb = gainReductionDb;
                double gainLinear = Math.Pow(10, -gainReductionDb / 20.0) * makeupLinear;

                for (int c = 0; c < channels; c++)
                {
                    float v = (float)(buffer[baseIdx + c] * gainLinear);
                    if (v > 1f) v = 1f;
                    else if (v < -1f) v = -1f;
                    buffer[baseIdx + c] = v;
                }
            }
        }

        public void Reset()
        {
            _envelope = 0;
            CurrentGainReductionDb = 0;
        }

        public Dictionary<string, double> GetParameters() => new()
        {
            ["ThresholdDb"] = ThresholdDb,
            ["Ratio"] = Ratio,
            ["AttackMs"] = AttackMs,
            ["ReleaseMs"] = ReleaseMs,
            ["MakeupDb"] = MakeupDb,
        };

        public void SetParameters(Dictionary<string, double> parameters)
        {
            if (parameters.TryGetValue("ThresholdDb", out var t)) ThresholdDb = t;
            if (parameters.TryGetValue("Ratio", out var r)) Ratio = r;
            if (parameters.TryGetValue("AttackMs", out var a)) AttackMs = a;
            if (parameters.TryGetValue("ReleaseMs", out var rl)) ReleaseMs = rl;
            if (parameters.TryGetValue("MakeupDb", out var m)) MakeupDb = m;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicFXApp.Audio.Effects
{
    /// <summary>
    /// Downward expander / noise gate: the mirror image of a compressor. Instead
    /// of squashing signal above a threshold, it attenuates signal below one -
    /// giving extra attenuation to residual hiss during pauses, on top of
    /// whatever the noise suppressor already removed. Linked (mono-summed)
    /// detection keeps stereo signals from shifting image as it opens/closes.
    /// </summary>
    public class ExpanderGateEffect : IAudioEffect
    {
        public string DisplayName => "Expander / Gate";
        public string BadgeText => "GATE";
        public string AccentHex => "#7ED957";

        private bool _enabled = true;
        public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }

        private double _thresholdDb = -45;
        public double ThresholdDb
        {
            get => _thresholdDb;
            set { _thresholdDb = Math.Clamp(value, -80, 0); OnPropertyChanged(); }
        }

        private double _ratio = 4.0;
        public double Ratio
        {
            get => _ratio;
            set { _ratio = Math.Clamp(value, 1, 20); OnPropertyChanged(); }
        }

        private double _attackMs = 2;
        /// <summary>How fast the gate closes when signal drops below threshold.</summary>
        public double AttackMs
        {
            get => _attackMs;
            set { _attackMs = Math.Clamp(value, 0.1, 50); OnPropertyChanged(); }
        }

        private double _releaseMs = 150;
        /// <summary>How fast the gate re-opens when signal rises back above threshold.</summary>
        public double ReleaseMs
        {
            get => _releaseMs;
            set { _releaseMs = Math.Clamp(value, 10, 1000); OnPropertyChanged(); }
        }

        private double _rangeDb = 30;
        /// <summary>Maximum attenuation applied when fully closed (dB). Keeps the gate from creating dead silence.</summary>
        public double RangeDb
        {
            get => _rangeDb;
            set { _rangeDb = Math.Clamp(value, 0, 60); OnPropertyChanged(); }
        }

        private double _envelope; // linear, smoothed detector level
        private double _currentAttenuationDb;

        public void Process(float[] buffer, int channels, int sampleRate)
        {
            if (channels < 1) channels = 1;
            int frames = buffer.Length / channels;

            double attackCoeff = Math.Exp(-1.0 / (sampleRate * (_attackMs / 1000.0)));
            double releaseCoeff = Math.Exp(-1.0 / (sampleRate * (_releaseMs / 1000.0)));

            for (int f = 0; f < frames; f++)
            {
                int baseIdx = f * channels;

                double peak = 0;
                for (int c = 0; c < channels; c++)
                {
                    double a = Math.Abs(buffer[baseIdx + c]);
                    if (a > peak) peak = a;
                }

                if (peak > _envelope)
                    _envelope = releaseCoeff * _envelope + (1 - releaseCoeff) * peak; // signal rising -> gate opens at release speed
                else
                    _envelope = attackCoeff * _envelope + (1 - attackCoeff) * peak;   // signal falling -> gate closes at attack speed

                double levelDb = 20 * Math.Log10(Math.Max(_envelope, 1e-9));
                double targetAttenuationDb = 0;
                if (levelDb < _thresholdDb)
                {
                    double underDb = _thresholdDb - levelDb;
                    targetAttenuationDb = Math.Min(underDb * (1 - 1.0 / _ratio), _rangeDb);
                }

                _currentAttenuationDb = targetAttenuationDb > _currentAttenuationDb
                    ? attackCoeff * _currentAttenuationDb + (1 - attackCoeff) * targetAttenuationDb
                    : releaseCoeff * _currentAttenuationDb + (1 - releaseCoeff) * targetAttenuationDb;

                double gainLinear = Math.Pow(10, -_currentAttenuationDb / 20.0);
                for (int c = 0; c < channels; c++)
                    buffer[baseIdx + c] = (float)(buffer[baseIdx + c] * gainLinear);
            }
        }

        public void Reset()
        {
            _envelope = 0;
            _currentAttenuationDb = 0;
        }

        public Dictionary<string, double> GetParameters() => new()
        {
            ["ThresholdDb"] = ThresholdDb,
            ["Ratio"] = Ratio,
            ["AttackMs"] = AttackMs,
            ["ReleaseMs"] = ReleaseMs,
            ["RangeDb"] = RangeDb,
        };

        public void SetParameters(Dictionary<string, double> parameters)
        {
            if (parameters.TryGetValue("ThresholdDb", out var t)) ThresholdDb = t;
            if (parameters.TryGetValue("Ratio", out var r)) Ratio = r;
            if (parameters.TryGetValue("AttackMs", out var a)) AttackMs = a;
            if (parameters.TryGetValue("ReleaseMs", out var rl)) ReleaseMs = rl;
            if (parameters.TryGetValue("RangeDb", out var rg)) RangeDb = rg;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

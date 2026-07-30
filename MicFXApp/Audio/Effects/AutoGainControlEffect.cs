using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicFXApp.Audio.Effects
{
    /// <summary>
    /// Slow-acting automatic gain control: tracks a smoothed RMS envelope of the
    /// signal and nudges gain toward a target level, deliberately with a slow
    /// time constant so it doesn't "pump" room noise up during pauses (a fast
    /// AGC would raise the noise floor every time you stop talking).
    /// </summary>
    public class AutoGainControlEffect : IAudioEffect
    {
        public string DisplayName => "Auto Gain Control";
        public string BadgeText => "AGC";
        public string AccentHex => "#5AA9E6";

        private bool _enabled = true;
        public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }

        private double _targetDb = -18;
        public double TargetDb
        {
            get => _targetDb;
            set { _targetDb = Math.Clamp(value, -40, 0); OnPropertyChanged(); }
        }

        private double _maxBoostDb = 18;
        public double MaxBoostDb
        {
            get => _maxBoostDb;
            set { _maxBoostDb = Math.Clamp(value, 0, 24); OnPropertyChanged(); }
        }

        private double _responseMs = 500;
        /// <summary>How slowly the gain adapts. Higher = smoother but slower to react.</summary>
        public double ResponseMs
        {
            get => _responseMs;
            set { _responseMs = Math.Clamp(value, 100, 2000); OnPropertyChanged(); }
        }

        private double _msEnvelope; // mean-square, linear
        private double _currentGainDb;

        public void Process(float[] buffer, int channels, int sampleRate)
        {
            if (channels < 1) channels = 1;
            int frames = buffer.Length / channels;
            double coeff = Math.Exp(-1.0 / (sampleRate * (_responseMs / 1000.0)));

            for (int f = 0; f < frames; f++)
            {
                int baseIdx = f * channels;

                double frameMs = 0;
                for (int c = 0; c < channels; c++)
                {
                    double s = buffer[baseIdx + c];
                    frameMs += s * s;
                }
                frameMs /= channels;

                _msEnvelope = coeff * _msEnvelope + (1 - coeff) * frameMs;
                double rmsDb = 10 * Math.Log10(Math.Max(_msEnvelope, 1e-12));

                double desiredGainDb = Math.Clamp(_targetDb - rmsDb, -6, _maxBoostDb);
                _currentGainDb = coeff * _currentGainDb + (1 - coeff) * desiredGainDb;

                double gainLinear = Math.Pow(10, _currentGainDb / 20.0);
                for (int c = 0; c < channels; c++)
                {
                    float v = (float)(buffer[baseIdx + c] * gainLinear);
                    if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                    buffer[baseIdx + c] = v;
                }
            }
        }

        public void Reset()
        {
            _msEnvelope = 0;
            _currentGainDb = 0;
        }

        public Dictionary<string, double> GetParameters() => new()
        {
            ["TargetDb"] = TargetDb,
            ["MaxBoostDb"] = MaxBoostDb,
            ["ResponseMs"] = ResponseMs,
        };

        public void SetParameters(Dictionary<string, double> parameters)
        {
            if (parameters.TryGetValue("TargetDb", out var t)) TargetDb = t;
            if (parameters.TryGetValue("MaxBoostDb", out var m)) MaxBoostDb = m;
            if (parameters.TryGetValue("ResponseMs", out var r)) ResponseMs = r;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

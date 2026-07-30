using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicFXApp.Audio.Effects
{
    /// <summary>
    /// Wideband-keyed de-esser: a bandpass filter isolates the sibilance range
    /// (default ~6kHz) purely to *detect* harsh "s"/"sh" energy - that detector
    /// signal is never itself output. When it crosses the threshold, the full
    /// (unfiltered) signal gets gain-reduced, the same way a compressor would,
    /// but only in response to sibilance rather than overall loudness.
    /// </summary>
    public class DeEsserEffect : IAudioEffect
    {
        public string DisplayName => "De-Esser";
        public string BadgeText => "DS";
        public string AccentHex => "#4FD1E8";

        private bool _enabled = true;
        public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }

        private double _frequencyHz = 6000;
        public double FrequencyHz
        {
            get => _frequencyHz;
            set { _frequencyHz = Math.Clamp(value, 2000, 10000); _dirty = true; OnPropertyChanged(); }
        }

        private double _thresholdDb = -24;
        public double ThresholdDb
        {
            get => _thresholdDb;
            set { _thresholdDb = Math.Clamp(value, -60, 0); OnPropertyChanged(); }
        }

        private double _ratio = 4.0;
        public double Ratio
        {
            get => _ratio;
            set { _ratio = Math.Clamp(value, 1, 10); OnPropertyChanged(); }
        }

        private BiquadFilter[] _detectorFilters = Array.Empty<BiquadFilter>();
        private int _lastChannels = -1, _lastSampleRate = -1;
        private volatile bool _dirty = true;

        private const double AttackMs = 3;
        private const double ReleaseMs = 60;
        private double _envelope;

        private void EnsureFilters(int channels, int sampleRate)
        {
            if (_detectorFilters.Length == channels && _lastSampleRate == sampleRate && !_dirty) return;

            if (_detectorFilters.Length != channels)
            {
                _detectorFilters = new BiquadFilter[channels];
                for (int c = 0; c < channels; c++) _detectorFilters[c] = new BiquadFilter();
            }

            foreach (var f in _detectorFilters) f.SetBandPass(_frequencyHz, 2.5, sampleRate);

            _lastChannels = channels;
            _lastSampleRate = sampleRate;
            _dirty = false;
        }

        public void Process(float[] buffer, int channels, int sampleRate)
        {
            if (channels < 1) channels = 1;
            EnsureFilters(channels, sampleRate);
            int frames = buffer.Length / channels;

            double attackCoeff = Math.Exp(-1.0 / (sampleRate * (AttackMs / 1000.0)));
            double releaseCoeff = Math.Exp(-1.0 / (sampleRate * (ReleaseMs / 1000.0)));

            for (int f = 0; f < frames; f++)
            {
                int baseIdx = f * channels;

                double detectPeak = 0;
                for (int c = 0; c < channels; c++)
                {
                    // Run the bandpass purely for detection - its output is discarded, not written back.
                    double filtered = Math.Abs(_detectorFilters[c].Process(buffer[baseIdx + c]));
                    if (filtered > detectPeak) detectPeak = filtered;
                }

                if (detectPeak > _envelope)
                    _envelope = attackCoeff * _envelope + (1 - attackCoeff) * detectPeak;
                else
                    _envelope = releaseCoeff * _envelope + (1 - releaseCoeff) * detectPeak;

                double levelDb = 20 * Math.Log10(Math.Max(_envelope, 1e-9));
                double gainReductionDb = 0;
                if (levelDb > _thresholdDb)
                {
                    double overDb = levelDb - _thresholdDb;
                    gainReductionDb = overDb - overDb / _ratio;
                }

                double gainLinear = Math.Pow(10, -gainReductionDb / 20.0);
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
            foreach (var f in _detectorFilters) f.Reset();
            _envelope = 0;
        }

        public Dictionary<string, double> GetParameters() => new()
        {
            ["FrequencyHz"] = FrequencyHz,
            ["ThresholdDb"] = ThresholdDb,
            ["Ratio"] = Ratio,
        };

        public void SetParameters(Dictionary<string, double> parameters)
        {
            if (parameters.TryGetValue("FrequencyHz", out var f)) FrequencyHz = f;
            if (parameters.TryGetValue("ThresholdDb", out var t)) ThresholdDb = t;
            if (parameters.TryGetValue("Ratio", out var r)) Ratio = r;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

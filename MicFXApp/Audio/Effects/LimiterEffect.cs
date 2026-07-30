using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicFXApp.Audio.Effects
{
    /// <summary>
    /// Brick-wall peak limiter: near-instant gain reduction the moment any
    /// sample would exceed the ceiling, with a smoothed release back to unity
    /// gain so it doesn't pump. Meant to go last in the chain as a safety net
    /// against clipping, not as a tone-shaping tool (that's the Compressor).
    /// A final hard clamp is applied as a belt-and-suspenders backstop.
    /// </summary>
    public class LimiterEffect : IAudioEffect
    {
        public string DisplayName => "Limiter";
        public string BadgeText => "LIM";
        public string AccentHex => "#FF5C5C";

        private bool _enabled = true;
        public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }

        private double _ceilingDb = -1;
        public double CeilingDb
        {
            get => _ceilingDb;
            set { _ceilingDb = Math.Clamp(value, -12, 0); OnPropertyChanged(); }
        }

        private double _releaseMs = 80;
        public double ReleaseMs
        {
            get => _releaseMs;
            set { _releaseMs = Math.Clamp(value, 10, 500); OnPropertyChanged(); }
        }

        private double _currentGain = 1.0; // linear

        public void Process(float[] buffer, int channels, int sampleRate)
        {
            if (channels < 1) channels = 1;
            int frames = buffer.Length / channels;
            double ceiling = Math.Pow(10, _ceilingDb / 20.0);
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

                double neededGain = peak > ceiling ? ceiling / peak : 1.0;

                _currentGain = neededGain < _currentGain
                    ? neededGain // clamp down instantly - no attack smoothing, this is the whole point of a limiter
                    : releaseCoeff * _currentGain + (1 - releaseCoeff) * neededGain;

                for (int c = 0; c < channels; c++)
                {
                    float v = (float)(buffer[baseIdx + c] * _currentGain);
                    // Hard safety clamp in case a single sample still slips past (e.g. right at the envelope's attack instant).
                    if (v > ceiling) v = (float)ceiling;
                    else if (v < -ceiling) v = (float)-ceiling;
                    buffer[baseIdx + c] = v;
                }
            }
        }

        public void Reset()
        {
            _currentGain = 1.0;
        }

        public Dictionary<string, double> GetParameters() => new()
        {
            ["CeilingDb"] = CeilingDb,
            ["ReleaseMs"] = ReleaseMs,
        };

        public void SetParameters(Dictionary<string, double> parameters)
        {
            if (parameters.TryGetValue("CeilingDb", out var c)) CeilingDb = c;
            if (parameters.TryGetValue("ReleaseMs", out var r)) ReleaseMs = r;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

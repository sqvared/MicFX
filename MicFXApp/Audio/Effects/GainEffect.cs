using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicFXApp.Audio.Effects
{
    public class GainEffect : IAudioEffect
    {
        public string DisplayName => "Gain";
        public string BadgeText => "G";
        public string AccentHex => "#2DD4BF";

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        private double _gainDb = 0.0;
        /// <summary>Gain in decibels, range -24..+24.</summary>
        public double GainDb
        {
            get => _gainDb;
            set { _gainDb = Math.Clamp(value, -24, 24); OnPropertyChanged(); }
        }

        public void Process(float[] buffer, int channels, int sampleRate)
        {
            float linear = (float)Math.Pow(10, _gainDb / 20.0);
            for (int i = 0; i < buffer.Length; i++)
            {
                float v = buffer[i] * linear;
                // soft clip to avoid harsh digital clipping if pushed too hot
                if (v > 1f) v = 1f - (float)Math.Exp(-(v - 1f));
                else if (v < -1f) v = -1f + (float)Math.Exp((v + 1f));
                buffer[i] = v;
            }
        }

        public void Reset() { }

        public Dictionary<string, double> GetParameters() => new() { ["GainDb"] = GainDb };
        public void SetParameters(Dictionary<string, double> parameters)
        {
            if (parameters.TryGetValue("GainDb", out var v)) GainDb = v;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

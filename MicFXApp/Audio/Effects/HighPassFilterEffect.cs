using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicFXApp.Audio.Effects
{
    /// <summary>
    /// Low-cut filter: removes sub-sonic rumble, handling noise, and mic-stand
    /// thumps below the cutoff. This is the first, cheapest line of defense
    /// against noise a suppressor further down the chain can't easily learn.
    /// </summary>
    public class HighPassFilterEffect : IAudioEffect
    {
        public string DisplayName => "High-Pass Filter";
        public string BadgeText => "HPF";
        public string AccentHex => "#F5C242";

        private bool _enabled = true;
        public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }

        private double _cutoffHz = 100;
        /// <summary>Cutoff frequency in Hz, range 20..500. Everything below this rolls off.</summary>
        public double CutoffHz
        {
            get => _cutoffHz;
            set { _cutoffHz = Math.Clamp(value, 20, 500); _dirty = true; OnPropertyChanged(); }
        }

        private BiquadFilter[] _filters = Array.Empty<BiquadFilter>();
        private int _lastChannels = -1, _lastSampleRate = -1;
        private volatile bool _dirty = true;

        private void EnsureFilters(int channels, int sampleRate)
        {
            if (_filters.Length == channels && _lastSampleRate == sampleRate && !_dirty) return;

            if (_filters.Length != channels)
            {
                _filters = new BiquadFilter[channels];
                for (int c = 0; c < channels; c++) _filters[c] = new BiquadFilter();
            }

            foreach (var f in _filters) f.SetHighPass(_cutoffHz, 0.707, sampleRate);

            _lastChannels = channels;
            _lastSampleRate = sampleRate;
            _dirty = false;
        }

        public void Process(float[] buffer, int channels, int sampleRate)
        {
            EnsureFilters(channels, sampleRate);
            for (int i = 0; i < buffer.Length; i++)
            {
                int ch = i % channels;
                buffer[i] = _filters[ch].Process(buffer[i]);
            }
        }

        public void Reset()
        {
            foreach (var f in _filters) f.Reset();
        }

        public Dictionary<string, double> GetParameters() => new() { ["CutoffHz"] = CutoffHz };
        public void SetParameters(Dictionary<string, double> parameters)
        {
            if (parameters.TryGetValue("CutoffHz", out var v)) CutoffHz = v;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

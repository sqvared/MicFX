using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicFXApp.Audio.Effects
{
    public class EqBand : INotifyPropertyChanged
    {
        public string Label { get; }
        public double FreqHz { get; }
        public BandKind Kind { get; }

        private double _gainDb;
        public double GainDb
        {
            get => _gainDb;
            set { _gainDb = value; OnPropertyChanged(); Changed?.Invoke(); }
        }

        private double _q = 1.0;
        public double Q
        {
            get => _q;
            set { _q = value; OnPropertyChanged(); Changed?.Invoke(); }
        }

        public bool ShowQ => Kind == BandKind.Peaking;

        internal Action? Changed;

        public EqBand(string label, double freqHz, BandKind kind)
        {
            Label = label;
            FreqHz = freqHz;
            Kind = kind;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public enum BandKind { LowShelf, Peaking, HighShelf }

    /// <summary>
    /// 7-band graphic/parametric equalizer. Low and high bands are shelving filters,
    /// the middle bands are peaking (bell) filters. Each band keeps a per-channel
    /// biquad so stereo signals get independent filter state per channel.
    /// </summary>
    public class EqualizerEffect : IAudioEffect
    {
        public string DisplayName => "Equalizer";
        public string BadgeText => "EQ";
        public string AccentHex => "#8B7FFF";

        private bool _enabled = true;
        public bool Enabled
        {
            get => _enabled;
            set { _enabled = value; OnPropertyChanged(); }
        }

        public ObservableCollection<EqBand> Bands { get; } = new()
        {
            new EqBand("60 Hz",    60,    BandKind.LowShelf),
            new EqBand("150 Hz",   150,   BandKind.Peaking),
            new EqBand("400 Hz",   400,   BandKind.Peaking),
            new EqBand("1 kHz",    1000,  BandKind.Peaking),
            new EqBand("2.5 kHz",  2500,  BandKind.Peaking),
            new EqBand("6 kHz",    6000,  BandKind.Peaking),
            new EqBand("12 kHz",   12000, BandKind.HighShelf),
        };

        // filters[bandIndex][channel]
        private BiquadFilter[][] _filters;
        private int _lastSampleRate = -1;
        private int _lastChannels = -1;

        public EqualizerEffect()
        {
            _filters = Array.Empty<BiquadFilter[]>();
            foreach (var b in Bands) b.Changed = () => _coeffsDirty = true;
        }

        private volatile bool _coeffsDirty = true;

        private void EnsureFilters(int channels, int sampleRate)
        {
            if (_filters.Length == Bands.Count && _lastChannels == channels && _lastSampleRate == sampleRate && !_coeffsDirty)
                return;

            if (_filters.Length != Bands.Count || _lastChannels != channels)
            {
                _filters = new BiquadFilter[Bands.Count][];
                for (int b = 0; b < Bands.Count; b++)
                {
                    _filters[b] = new BiquadFilter[channels];
                    for (int c = 0; c < channels; c++) _filters[b][c] = new BiquadFilter();
                }
            }

            for (int b = 0; b < Bands.Count; b++)
            {
                var band = Bands[b];
                for (int c = 0; c < channels; c++)
                {
                    var f = _filters[b][c];
                    switch (band.Kind)
                    {
                        case BandKind.LowShelf:
                            f.SetLowShelf(band.FreqHz, band.GainDb, 0.7, sampleRate);
                            break;
                        case BandKind.HighShelf:
                            f.SetHighShelf(band.FreqHz, band.GainDb, 0.7, sampleRate);
                            break;
                        default:
                            f.SetPeaking(band.FreqHz, band.GainDb, band.Q, sampleRate);
                            break;
                    }
                }
            }

            _lastSampleRate = sampleRate;
            _lastChannels = channels;
            _coeffsDirty = false;
        }

        public void Process(float[] buffer, int channels, int sampleRate)
        {
            EnsureFilters(channels, sampleRate);

            for (int b = 0; b < Bands.Count; b++)
            {
                if (Bands[b].GainDb == 0) continue; // skip flat bands, cheap optimization
                var chFilters = _filters[b];
                for (int i = 0; i < buffer.Length; i++)
                {
                    int ch = i % channels;
                    buffer[i] = chFilters[ch].Process(buffer[i]);
                }
            }
        }

        public void Reset()
        {
            foreach (var bandFilters in _filters)
                foreach (var f in bandFilters)
                    f.Reset();
        }

        public Dictionary<string, double> GetParameters()
        {
            var p = new Dictionary<string, double>();
            for (int b = 0; b < Bands.Count; b++) p[$"Band{b}"] = Bands[b].GainDb;
            return p;
        }

        public void SetParameters(Dictionary<string, double> parameters)
        {
            for (int b = 0; b < Bands.Count; b++)
                if (parameters.TryGetValue($"Band{b}", out var v)) Bands[b].GainDb = v;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

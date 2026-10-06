using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MicFXApp.Audio.Effects;

/// <summary>Streaming RNNoise with independent mono states and latency-preserving dry/wet bypass.
/// Process, Prepare, Reset and Dispose must be serialized by the audio engine.</summary>
public class NoiseSuppressorEffect : IAudioEffect, IDisposable
{
    public string DisplayName => "Noise Suppressor";
    public string BadgeText => "NS";
    public string AccentHex => "#FF5C9E";
    private volatile bool _enabled = true;
    public bool Enabled { get => _enabled; set { _enabled = value; OnPropertyChanged(); } }
    private double _amount = 60;
    /// <summary>Continuous, delay-aligned dry/wet strength. Zero is delayed unity.</summary>
    public double Amount
    {
        get => _amount;
        set { _amount = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0; OnPropertyChanged(); }
    }
    private sealed class Channel : IDisposable
    {
        internal readonly RnNoiseProcessor Noise;
        internal readonly float[] Dry;
        internal int Position;
        private readonly StreamingResampler? _to48, _from48;
        private int _startup;
        internal Channel(int latency, int rate)
        {
            Dry = new float[latency];
            if (rate != 48000)
            {
                int inputDelay = StreamingResampler.Radius(rate, 48000);
                _to48 = new StreamingResampler(rate, 48000, inputDelay);
                _startup = Math.Max(2, (int)Math.Ceiling(rate / 48000.0));
                double outputDelay = (latency - _startup - inputDelay) * 48000.0 / rate - RnNoiseProcessor.Latency;
                _from48 = new StreamingResampler(48000, rate, outputDelay);
            }
            // Acquire native ownership last: managed allocation failures cannot leak it.
            Noise = new RnNoiseProcessor();
        }
        internal float Process(float sample)
        {
            if (_to48 == null) return Noise.Process(sample);
            _to48.Push(sample);
            while (_to48.TryRead(out float converted)) _from48!.Push(Noise.Process(converted));
            if (_startup > 0) { _startup--; return 0; }
            if (!_from48!.TryRead(out float result)) throw new InvalidOperationException("Output sample clock underrun.");
            return result;
        }
        public void Dispose() => Noise.Dispose();
    }
    private Channel[] _channels = Array.Empty<Channel>();
    private int _rate;
    private bool _disposed;
    private double _mix;
    public int LatencySamples { get; private set; }
    public void Prepare(int channels, int sampleRate)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (channels < 1 || channels > 32) throw new ArgumentOutOfRangeException(nameof(channels));
        if (sampleRate < 8000 || sampleRate > 192000) throw new ArgumentOutOfRangeException(nameof(sampleRate), "Supported sample rates: 8000–192000 Hz.");
        if (_channels.Length == channels && _rate == sampleRate) return;
        Release();
        _rate = sampleRate;
        LatencySamples = sampleRate == 48000 ? RnNoiseProcessor.Latency : Math.Max(2, (int)Math.Ceiling(sampleRate / 48000.0)) + (int)Math.Ceiling(
            StreamingResampler.Radius(sampleRate, 48000) +
            (RnNoiseProcessor.Latency + StreamingResampler.Radius(48000, sampleRate)) * sampleRate / 48000.0);
        _channels = new Channel[channels];
        try { for (int c = 0; c < channels; c++) _channels[c] = new Channel(LatencySamples, sampleRate); }
        catch { Release(); throw; }
        _mix = Enabled ? Amount / 100 : 0;
    }
    public void Process(float[] buffer, int channels, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (channels < 1 || buffer.Length % channels != 0) throw new ArgumentException("Buffer must contain whole interleaved frames.");
        Prepare(channels, sampleRate);
        double target = Enabled ? Amount / 100 : 0;
        double step = 1.0 / (sampleRate * .01); // bounded 10ms full-scale transition
        for (int i = 0; i < buffer.Length; i += channels)
        {
            _mix += Math.Clamp(target - _mix, -step, step);
            for (int c = 0; c < channels; c++)
            {
                var state = _channels[c];
                float input = float.IsFinite(buffer[i+c]) ? buffer[i+c] : 0;
                float dry = state.Dry[state.Position];
                state.Dry[state.Position] = input;
                state.Position = (state.Position + 1) % state.Dry.Length;
                float wet = state.Process(Math.Clamp(input, -1, 1));
                buffer[i+c] = (float)(dry * (1 - _mix) + wet * _mix);
            }
        }
    }
    private void Release()
    {
        foreach (var channel in _channels) channel?.Dispose();
        _channels = Array.Empty<Channel>();
    }
    public void Reset()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int channels = _channels.Length, rate = _rate;
        Release();
        if (channels > 0) Prepare(channels, rate);
    }
    public void Dispose() { if (_disposed) return; Release(); _disposed = true; }
    public Dictionary<string, double> GetParameters() => new() { ["Amount"] = Amount };
    public void SetParameters(Dictionary<string, double> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.TryGetValue("Amount", out var value)) Amount = value;
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

using System;
using RNNoise.NET;

namespace MicFXApp.Audio.Effects;

/// <summary>One persistent mono RNNoise state. All calls contain exactly 480 normalized samples.</summary>
internal sealed class RnNoiseProcessor : IDisposable
{
    internal const int FrameSize = 480;
    // Measured with an impulse against the pinned 0.1.9 native binary: 480 samples.
    internal const int NativeDelay = 480;
    internal const int Latency = FrameSize + NativeDelay;
    private readonly float[] _frame = new float[FrameSize];
    private readonly float[] _output = new float[FrameSize];
    private readonly Denoiser _denoiser = new();
    private int _position;
    public float Process(float sample)
    {
        float result = _output[_position];
        _frame[_position++] = sample;
        if (_position == FrameSize)
        {
            _denoiser.Denoise(_frame.AsSpan(), finish: false);
            Array.Copy(_frame, _output, FrameSize);
            _position = 0;
        }
        return float.IsFinite(result) ? result : 0;
    }
    public void Dispose() => _denoiser.Dispose();
}

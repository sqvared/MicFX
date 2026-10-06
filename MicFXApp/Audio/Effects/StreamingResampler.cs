using System;

namespace MicFXApp.Audio.Effects;

/// <summary>Causal, band-limited streaming sample-rate converter. A windowed-sinc
/// low-pass (64 taps at unity, expanded for decimation) prevents aliasing.
/// Rational sample clocks make scheduling independent of callback boundaries.
/// Kernel tables and bounded rings are allocated only during preparation.</summary>
internal sealed class StreamingResampler
{
    private const int Phases = 1024;
    private readonly int _inputRate, _outputRate, _radius, _taps;
    private readonly double _delay;
    private readonly float[] _history = new float[1024];
    private readonly float[] _queue = new float[32];
    private readonly float[] _kernel;
    private long _inputIndex = -1, _nextNumerator;
    private int _read, _write, _count;
    internal static int Radius(int inputRate, int outputRate) => (int)Math.Ceiling(32.0 / Math.Min(1.0, (double)outputRate / inputRate));
    internal StreamingResampler(int inputRate, int outputRate, double delay)
    {
        _inputRate = inputRate; _outputRate = outputRate; _delay = delay;
        _radius = Radius(inputRate, outputRate); _taps = _radius * 2 + 1;
        _kernel = new float[(Phases + 1) * _taps];
        double cutoff = .94 * Math.Min(1.0, (double)outputRate / inputRate);
        for (int p = 0; p <= Phases; p++)
        {
            double sum = 0;
            for (int k = 0; k < _taps; k++)
            {
                double x = k - _radius - (double)p / Phases;
                double z = Math.PI * x * cutoff;
                double window = Math.Abs(x) >= _radius ? 0 : .42 + .5 * Math.Cos(Math.PI*x/_radius) + .08*Math.Cos(2*Math.PI*x/_radius);
                double value = (Math.Abs(z) < 1e-12 ? cutoff : cutoff*Math.Sin(z)/z) * window;
                _kernel[p*_taps+k] = (float)value; sum += value;
            }
            for (int k = 0; k < _taps; k++) _kernel[p*_taps+k] /= (float)sum;
        }
    }
    internal void Push(float sample)
    {
        _history[(int)(++_inputIndex % _history.Length)] = sample;
        while (_nextNumerator <= _inputIndex * _outputRate)
        {
            double time = (double)_nextNumerator / _outputRate - _delay;
            long center = (long)Math.Floor(time);
            double phase = (time-center)*Phases;
            int p = Math.Min((int)phase, Phases-1);
            double fraction = phase-p, sum = 0;
            for (int k = 0; k < _taps; k++)
            {
                long index = center + k - _radius;
                if (index < 0) continue;
                if (index > _inputIndex || _inputIndex-index >= _history.Length) throw new InvalidOperationException("Resampler history underrun.");
                double weight = _kernel[p*_taps+k]*(1-fraction) + _kernel[(p+1)*_taps+k]*fraction;
                sum += _history[(int)(index % _history.Length)] * weight;
            }
            if (_count == _queue.Length) throw new InvalidOperationException("Resampler queue overflow.");
            _queue[_write] = (float)sum; _write = (_write+1)%_queue.Length; _count++;
            _nextNumerator += _inputRate;
        }
    }
    internal bool TryRead(out float value)
    {
        if (_count == 0) { value = 0; return false; }
        value = _queue[_read]; _read = (_read+1)%_queue.Length; _count--; return true;
    }
}

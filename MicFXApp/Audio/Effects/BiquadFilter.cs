using System;

namespace MicFXApp.Audio.Effects
{
    /// <summary>
    /// Standard "RBJ cookbook" biquad filter. One instance = one channel's worth of state,
    /// so stereo EQ bands should keep one BiquadFilter per channel.
    /// </summary>
    public class BiquadFilter
    {
        private double _b0, _b1, _b2, _a1, _a2;
        private double _x1, _x2, _y1, _y2;

        public void SetPeaking(double freqHz, double gainDb, double q, int sampleRate)
        {
            double A = Math.Pow(10, gainDb / 40.0);
            double w0 = 2 * Math.PI * freqHz / sampleRate;
            double alpha = Math.Sin(w0) / (2 * q);
            double cosw0 = Math.Cos(w0);

            double b0 = 1 + alpha * A;
            double b1 = -2 * cosw0;
            double b2 = 1 - alpha * A;
            double a0 = 1 + alpha / A;
            double a1 = -2 * cosw0;
            double a2 = 1 - alpha / A;

            Normalize(b0, b1, b2, a0, a1, a2);
        }

        public void SetLowShelf(double freqHz, double gainDb, double q, int sampleRate)
        {
            double A = Math.Pow(10, gainDb / 40.0);
            double w0 = 2 * Math.PI * freqHz / sampleRate;
            double cosw0 = Math.Cos(w0);
            double sinw0 = Math.Sin(w0);
            double alpha = sinw0 / 2 * Math.Sqrt((A + 1 / A) * (1 / q - 1) + 2);
            double twoSqrtAalpha = 2 * Math.Sqrt(A) * alpha;

            double b0 = A * ((A + 1) - (A - 1) * cosw0 + twoSqrtAalpha);
            double b1 = 2 * A * ((A - 1) - (A + 1) * cosw0);
            double b2 = A * ((A + 1) - (A - 1) * cosw0 - twoSqrtAalpha);
            double a0 = (A + 1) + (A - 1) * cosw0 + twoSqrtAalpha;
            double a1 = -2 * ((A - 1) + (A + 1) * cosw0);
            double a2 = (A + 1) + (A - 1) * cosw0 - twoSqrtAalpha;

            Normalize(b0, b1, b2, a0, a1, a2);
        }

        public void SetHighShelf(double freqHz, double gainDb, double q, int sampleRate)
        {
            double A = Math.Pow(10, gainDb / 40.0);
            double w0 = 2 * Math.PI * freqHz / sampleRate;
            double cosw0 = Math.Cos(w0);
            double sinw0 = Math.Sin(w0);
            double alpha = sinw0 / 2 * Math.Sqrt((A + 1 / A) * (1 / q - 1) + 2);
            double twoSqrtAalpha = 2 * Math.Sqrt(A) * alpha;

            double b0 = A * ((A + 1) + (A - 1) * cosw0 + twoSqrtAalpha);
            double b1 = -2 * A * ((A - 1) + (A + 1) * cosw0);
            double b2 = A * ((A + 1) + (A - 1) * cosw0 - twoSqrtAalpha);
            double a0 = (A + 1) - (A - 1) * cosw0 + twoSqrtAalpha;
            double a1 = 2 * ((A - 1) - (A + 1) * cosw0);
            double a2 = (A + 1) - (A - 1) * cosw0 - twoSqrtAalpha;

            Normalize(b0, b1, b2, a0, a1, a2);
        }

        public void SetHighPass(double freqHz, double q, int sampleRate)
        {
            double w0 = 2 * Math.PI * freqHz / sampleRate;
            double alpha = Math.Sin(w0) / (2 * q);
            double cosw0 = Math.Cos(w0);

            double b0 = (1 + cosw0) / 2;
            double b1 = -(1 + cosw0);
            double b2 = (1 + cosw0) / 2;
            double a0 = 1 + alpha;
            double a1 = -2 * cosw0;
            double a2 = 1 - alpha;

            Normalize(b0, b1, b2, a0, a1, a2);
        }

        /// <summary>Constant 0dB-peak-gain bandpass, used as a sidechain detector filter (e.g. de-esser).</summary>
        public void SetBandPass(double freqHz, double q, int sampleRate)
        {
            double w0 = 2 * Math.PI * freqHz / sampleRate;
            double alpha = Math.Sin(w0) / (2 * q);
            double cosw0 = Math.Cos(w0);

            double b0 = alpha;
            double b1 = 0;
            double b2 = -alpha;
            double a0 = 1 + alpha;
            double a1 = -2 * cosw0;
            double a2 = 1 - alpha;

            Normalize(b0, b1, b2, a0, a1, a2);
        }

        private void Normalize(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = a1 / a0;
            _a2 = a2 / a0;
        }

        public float Process(float input)
        {
            double x0 = input;
            double y0 = _b0 * x0 + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
            _x2 = _x1;
            _x1 = x0;
            _y2 = _y1;
            _y1 = y0;
            return (float)y0;
        }

        public void Reset()
        {
            _x1 = _x2 = _y1 = _y2 = 0;
        }
    }
}

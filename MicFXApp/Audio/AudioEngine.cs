using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using MicFXApp.Audio.Effects;

namespace MicFXApp.Audio
{
    /// <summary>
    /// Captures the physical microphone, runs the samples through an ordered
    /// chain of IAudioEffect nodes, and pushes the result to:
    ///  1) the selected "virtual cable" render device (so other apps can pick
    ///     that cable's recording endpoint as their microphone),
    ///  2) optionally, a monitor render device (e.g. your speakers/headphones)
    ///     so you can listen to the processed signal live, and
    ///  3) a live frequency-spectrum snapshot for the UI's analyzer display.
    ///
    /// Also runs an optional loopback capture of system playback to feed any
    /// AEC effect in the chain its reference signal.
    /// </summary>
    public class AudioEngine : IDisposable
    {
        public ObservableCollection<IAudioEffect> Chain { get; } = new();
        private readonly object _chainLock = new();

        /// <summary>Thread-safe add - use this instead of Chain.Add directly once the engine may be running.</summary>
        public void AddEffect(IAudioEffect effect)
        {
            string? preparationError = null;
            lock (_chainLock)
            {
                try
                {
                    if (IsRunning && _capture != null && effect is NoiseSuppressorEffect ns)
                        ns.Prepare(_capture.WaveFormat.Channels, _capture.WaveFormat.SampleRate);
                }
                catch (Exception ex)
                {
                    if (!Chain.Contains(effect) && effect is IDisposable disposable) disposable.Dispose();
                    preparationError = "Could not add effect: " + ex.Message;
                }
                if (preparationError == null)
                    Chain.Add(effect);
            }
            if (preparationError != null)
            {
                ErrorOccurred?.Invoke(this, preparationError);
                return;
            }
            UpdateLoopbackCaptureIfNeeded();
        }

        /// <summary>Thread-safe remove - use this instead of Chain.Remove directly once the engine may be running.</summary>
        public void RemoveEffect(IAudioEffect effect)
        {
            lock (_chainLock)
            {
                if (Chain.Remove(effect) && effect is IDisposable disposable)
                    disposable.Dispose();
            }
            UpdateLoopbackCaptureIfNeeded();
        }

        /// <summary>Publish a complete preset between callbacks, never a partial chain.</summary>
        public void ReplaceEffects(System.Collections.Generic.List<IAudioEffect> effects)
        {
            string? preparationError = null;
            lock (_chainLock)
            {
                try
                {
                    if (IsRunning && _capture != null)
                        foreach (var effect in effects)
                            if (effect is NoiseSuppressorEffect ns)
                                ns.Prepare(_capture.WaveFormat.Channels, _capture.WaveFormat.SampleRate);
                }
                catch (Exception ex)
                {
                    foreach (var effect in effects)
                        if (!Chain.Contains(effect) && effect is IDisposable disposable) disposable.Dispose();
                    preparationError = "Could not load preset: " + ex.Message;
                }
                if (preparationError == null)
                {
                    var previous = Chain.ToArray();
                    Chain.Clear();
                    foreach (var effect in effects) Chain.Add(effect);
                    foreach (var effect in previous)
                        if (!effects.Contains(effect) && effect is IDisposable disposable)
                            disposable.Dispose();
                }
            }
            if (preparationError != null)
            {
                ErrorOccurred?.Invoke(this, preparationError);
                return;
            }
            UpdateLoopbackCaptureIfNeeded();
        }

        /// <summary>Thread-safe reorder - use this instead of Chain.Move directly once the engine may be running.</summary>
        public void MoveEffect(int oldIndex, int newIndex)
        {
            lock (_chainLock) { Chain.Move(oldIndex, newIndex); }
        }

        private WasapiCapture? _capture;
        private WasapiOut? _cableOut;
        private BufferedWaveProvider? _cableBuffer;

        private readonly object _monitorLock = new();
        private WasapiOut? _monitorOut;
        private BufferedWaveProvider? _monitorBuffer;

        private WasapiLoopbackCapture? _loopbackCapture;

        private volatile bool _isRunning;
        public bool IsRunning => _isRunning;
        public bool MonitorEnabled { get; private set; }

        // Simple peak meters, read by the UI on a timer. Volatile because they're
        // written from the WASAPI capture callback thread.
        public volatile float InputPeak;
        public volatile float OutputPeak;

        public MMDevice? MicDevice { get; set; }
        public MMDevice? CableRenderDevice { get; set; }
        public MMDevice? MonitorRenderDevice { get; set; }

        public event EventHandler<string>? ErrorOccurred;

        public void Start()
        {
            if (IsRunning) return;
            if (MicDevice == null || CableRenderDevice == null)
            {
                ErrorOccurred?.Invoke(this, "Select both a microphone and a virtual cable output device first.");
                return;
            }

            try
            {
                _capture = new WasapiCapture(MicDevice) { ShareMode = AudioClientShareMode.Shared };
                var format = _capture.WaveFormat;

                // OnDataAvailable interprets the captured bytes as 32-bit IEEE float samples.
                // WASAPI's shared-mode mix format is virtually always float32 on modern Windows,
                // but if a driver ever negotiates something else, fail loudly here instead of
                // silently processing garbage audio.
                if (format.Encoding != WaveFormatEncoding.IeeeFloat || format.BitsPerSample != 32)
                {
                    ErrorOccurred?.Invoke(this,
                        $"This microphone's audio format ({format.Encoding}, {format.BitsPerSample}-bit) isn't supported - " +
                        "expected 32-bit IEEE float, which is what WASAPI shared mode normally provides. Try a different device.");
                    _capture.Dispose();
                    _capture = null;
                    return;
                }

                _cableBuffer = new BufferedWaveProvider(format)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromMilliseconds(500)
                };
                _cableOut = new WasapiOut(CableRenderDevice, AudioClientShareMode.Shared, true, 30);
                _cableOut.Init(_cableBuffer);

                if (MonitorEnabled && MonitorRenderDevice != null)
                    EnableMonitorInternal(MonitorRenderDevice, format);

                lock (_chainLock)
                {
                    foreach (var fx in Chain)
                    {
                        fx.Reset();
                        if (fx is NoiseSuppressorEffect ns)
                            ns.Prepare(format.Channels, format.SampleRate);
                    }
                }

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += (s, e) =>
                {
                    _isRunning = false;
                    if (e.Exception != null)
                        ErrorOccurred?.Invoke(this, "Capture stopped: " + e.Exception.Message);
                };

                _capture.StartRecording();
                _cableOut.Play();
                _isRunning = true;

                UpdateLoopbackCaptureIfNeeded();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, "Failed to start: " + ex.Message);
                Stop();
            }
        }

        public void Stop()
        {
            try { _capture?.StopRecording(); } catch { }
            try { _cableOut?.Stop(); } catch { }
            DisableMonitorInternal();
            try { _loopbackCapture?.StopRecording(); } catch { }
            _loopbackCapture?.Dispose();
            _loopbackCapture = null;

            _capture?.Dispose();
            _cableOut?.Dispose();

            _capture = null;
            _cableOut = null;
            _cableBuffer = null;
            _isRunning = false;
        }

        /// <summary>
        /// Toggle live monitoring. This is fully decoupled from the mic-capture/virtual-cable
        /// pipeline - it only ever creates or tears down the monitor output, never touches
        /// capture, so it can't get the engine into a broken state the way restarting
        /// everything could.
        /// </summary>
        public void SetMonitoring(bool enabled, MMDevice? monitorDevice)
        {
            MonitorEnabled = enabled;
            MonitorRenderDevice = monitorDevice;

            if (!IsRunning) return; // takes effect at next Start()

            if (enabled && monitorDevice != null && _capture != null)
                EnableMonitorInternal(monitorDevice, _capture.WaveFormat);
            else
                DisableMonitorInternal();
        }

        private void EnableMonitorInternal(MMDevice device, WaveFormat format)
        {
            lock (_monitorLock)
            {
                DisableMonitorInternalNoLock();
                try
                {
                    _monitorBuffer = new BufferedWaveProvider(format)
                    {
                        DiscardOnBufferOverflow = true,
                        BufferDuration = TimeSpan.FromMilliseconds(500)
                    };
                    _monitorOut = new WasapiOut(device, AudioClientShareMode.Shared, true, 30);
                    _monitorOut.Init(_monitorBuffer);
                    _monitorOut.Play();
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, "Could not start monitoring: " + ex.Message);
                    _monitorOut = null;
                    _monitorBuffer = null;
                }
            }
        }

        private void DisableMonitorInternal()
        {
            lock (_monitorLock) { DisableMonitorInternalNoLock(); }
        }

        private void DisableMonitorInternalNoLock()
        {
            try { _monitorOut?.Stop(); } catch { }
            try { _monitorOut?.Dispose(); } catch { }
            _monitorOut = null;
            _monitorBuffer = null;
        }

        // ---------------- AEC reference (loopback) capture ----------------

        private void UpdateLoopbackCaptureIfNeeded()
        {
            bool needsLoopback;
            lock (_chainLock) { needsLoopback = Chain.Any(fx => fx is IReferenceFedEffect); }

            if (needsLoopback && _loopbackCapture == null && IsRunning)
            {
                try
                {
                    using var enumerator = new MMDeviceEnumerator();
                    var defaultRender = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    _loopbackCapture = new WasapiLoopbackCapture(defaultRender);
                    _loopbackCapture.DataAvailable += OnLoopbackDataAvailable;
                    _loopbackCapture.StartRecording();
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, "Could not start echo-cancellation reference capture: " + ex.Message);
                    _loopbackCapture = null;
                }
            }
            else if (!needsLoopback && _loopbackCapture != null)
            {
                try { _loopbackCapture.StopRecording(); } catch { }
                _loopbackCapture.Dispose();
                _loopbackCapture = null;
            }
        }

        private void OnLoopbackDataAvailable(object? sender, WaveInEventArgs e)
        {
            var capture = _loopbackCapture;
            if (capture == null) return;
            var fmt = capture.WaveFormat;
            if (fmt.Encoding != WaveFormatEncoding.IeeeFloat || fmt.BitsPerSample != 32) return;

            int floatCount = e.BytesRecorded / 4;
            var samples = new float[floatCount];
            Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);

            IAudioEffect[] chainSnapshot;
            lock (_chainLock) { chainSnapshot = Chain.ToArray(); }

            foreach (var fx in chainSnapshot)
                if (fx is IReferenceFedEffect refFed)
                    refFed.SupplyReference(samples, fmt.Channels, fmt.SampleRate);
        }

        // ---------------- spectrum analyzer ----------------

        private const int SpectrumFftSize = 1024;
        private const int SpectrumLog2 = 10;
        private const int SpectrumBandCount = 48;
        private readonly float[] _spectrumRing = new float[SpectrumFftSize];
        private int _spectrumRingPos;
        private int _spectrumRingCount;
        private float[] _spectrumSnapshot = new float[SpectrumBandCount];

        /// <summary>Latest 48-band log-frequency magnitude snapshot (0-1 range), safe to read from any thread.</summary>
        public float[] GetSpectrumSnapshot() => _spectrumSnapshot;

        private void UpdateSpectrum(float[] samples, int channels, int sampleRate)
        {
            int frames = samples.Length / channels;
            for (int f = 0; f < frames; f++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++) sum += samples[f * channels + c];
                _spectrumRing[_spectrumRingPos] = sum / channels;
                _spectrumRingPos = (_spectrumRingPos + 1) % SpectrumFftSize;
                if (_spectrumRingCount < SpectrumFftSize) _spectrumRingCount++;
            }

            if (_spectrumRingCount < SpectrumFftSize) return;

            var fft = new Complex[SpectrumFftSize];
            for (int n = 0; n < SpectrumFftSize; n++)
            {
                int idx = (_spectrumRingPos + n) % SpectrumFftSize; // oldest-to-newest order
                double w = FastFourierTransform.HannWindow(n, SpectrumFftSize);
                fft[n].X = (float)(_spectrumRing[idx] * w);
                fft[n].Y = 0;
            }
            FastFourierTransform.FFT(true, SpectrumLog2, fft);

            int bins = SpectrumFftSize / 2;
            double minFreq = 40, maxFreq = Math.Min(sampleRate / 2.0, 16000);
            var bands = new float[SpectrumBandCount];

            for (int b = 0; b < SpectrumBandCount; b++)
            {
                double f0 = minFreq * Math.Pow(maxFreq / minFreq, (double)b / SpectrumBandCount);
                double f1 = minFreq * Math.Pow(maxFreq / minFreq, (double)(b + 1) / SpectrumBandCount);
                int k0 = Math.Max(1, (int)(f0 / sampleRate * SpectrumFftSize));
                int k1 = Math.Min(bins, Math.Max(k0 + 1, (int)(f1 / sampleRate * SpectrumFftSize)));

                double sum = 0; int count = 0;
                for (int k = k0; k < k1; k++)
                {
                    double mag = Math.Sqrt(fft[k].X * fft[k].X + fft[k].Y * fft[k].Y);
                    sum += mag;
                    count++;
                }
                double avgMag = count > 0 ? sum / count : 0;
                double db = 20 * Math.Log10(Math.Max(avgMag, 1e-9));
                bands[b] = (float)Math.Clamp((db + 70.0) / 70.0, 0, 1); // normalize -70..0 dB -> 0..1
            }

            Interlocked.Exchange(ref _spectrumSnapshot, bands);
        }

        // Serialize processing with removal/disposal: a snapshot alone does not
        // keep native denoiser state alive after an effect is removed by the UI.
        private void ProcessEffects(float[] samples, int channels, int sampleRate)
        {
            lock (_chainLock)
            {
                foreach (var effect in Chain)
                    // NS owns its latency-aligned bypass; keep its stream current.
                    if (effect.Enabled || effect is NoiseSuppressorEffect)
                        effect.Process(samples, channels, sampleRate);
            }
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (_capture == null) return;
            var format = _capture.WaveFormat;
            int floatCount = e.BytesRecorded / 4;
            float[] samples = new float[floatCount];
            Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);

            float inPeak = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float a = Math.Abs(samples[i]);
                if (a > inPeak) inPeak = a;
            }
            InputPeak = inPeak;

            ProcessEffects(samples, format.Channels, format.SampleRate);

            float outPeak = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float a = Math.Abs(samples[i]);
                if (a > outPeak) outPeak = a;
            }
            OutputPeak = outPeak;

            UpdateSpectrum(samples, format.Channels, format.SampleRate);

            byte[] outBytes = new byte[e.BytesRecorded];
            Buffer.BlockCopy(samples, 0, outBytes, 0, outBytes.Length);

            _cableBuffer?.AddSamples(outBytes, 0, outBytes.Length);

            lock (_monitorLock)
            {
                _monitorBuffer?.AddSamples(outBytes, 0, outBytes.Length);
            }
        }

        public static ObservableCollection<MMDevice> GetCaptureDevices()
        {
            var result = new ObservableCollection<MMDevice>();
            using var enumerator = new MMDeviceEnumerator();
            foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                result.Add(dev);
            return result;
        }

        public static ObservableCollection<MMDevice> GetRenderDevices()
        {
            var result = new ObservableCollection<MMDevice>();
            using var enumerator = new MMDeviceEnumerator();
            foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                result.Add(dev);
            return result;
        }

        public void Dispose()
        {
            Stop();
            lock (_chainLock)
            {
                foreach (var effect in Chain)
                    if (effect is IDisposable disposable) disposable.Dispose();
                Chain.Clear();
            }
        }
    }
}

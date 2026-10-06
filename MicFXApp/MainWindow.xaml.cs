using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using MicFXApp.Audio;
using MicFXApp.Audio.Effects;

namespace MicFXApp
{
    public partial class MainWindow : Window
    {
        private readonly AudioEngine _engine = new();
        private readonly DispatcherTimer _meterTimer;

        public MainWindow()
        {
            InitializeComponent();

            ChainList.DataContext = _engine.Chain;

            // Seed a sensible default chain so the app is useful out of the box.
            _engine.AddEffect(new GainEffect());
            _engine.AddEffect(new EqualizerEffect());
            _engine.AddEffect(new CompressorEffect());

            LoadDevices();

            _engine.ErrorOccurred += (s, msg) =>
                Dispatcher.Invoke(() => MessageBox.Show(msg, "MicFX", MessageBoxButton.OK, MessageBoxImage.Warning));

            _meterTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
            _meterTimer.Tick += (s, e) => UpdateMeters();
            _meterTimer.Start();

            Closing += (s, e) => _engine.Dispose();
            SourceInitialized += (s, e) => TryEnableDarkTitleBar();
        }

        // ---------------- dark title bar (Windows 10 1809+ / 11) ----------------
        [DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_20 = 20;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_19 = 19;

        private void TryEnableDarkTitleBar()
        {
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                int enable = 1;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_20, ref enable, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_19, ref enable, sizeof(int));
            }
            catch { /* cosmetic only */ }
        }

        // ---------------- device loading ----------------

        private void LoadDevices()
        {
            MicCombo.IsEnabled = false;
            CableCombo.IsEnabled = false;
            MonitorCombo.IsEnabled = false;

            // Enumerate on the UI thread deliberately - MMDevice/WASAPI are COM objects with
            // apartment affinity, and creating them on a Task.Run threadpool thread then later
            // activating WasapiCapture/WasapiOut on the UI thread caused "Specified cast is not
            // valid" (a COM marshaling failure across apartments). The lag fix doesn't actually
            // need a background thread - it just needs FriendlyName resolved ONCE into a cached
            // AudioDeviceItem instead of lazily every time the dropdown opens, which this still does.
            var captures = AudioEngine.GetCaptureDevices().Select(d => new AudioDeviceItem(d)).ToList();
            var renders = AudioEngine.GetRenderDevices().Select(d => new AudioDeviceItem(d)).ToList();

            MicCombo.ItemsSource = captures;
            if (captures.Count > 0) MicCombo.SelectedIndex = 0;

            CableCombo.ItemsSource = renders;
            var cableGuess = renders.FirstOrDefault(d => d.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase));
            CableCombo.SelectedItem = cableGuess ?? (renders.Count > 0 ? renders[0] : null);

            MonitorCombo.ItemsSource = renders;
            var defaultRender = renders.FirstOrDefault(d => !d.FriendlyName.Contains("CABLE", StringComparison.OrdinalIgnoreCase));
            MonitorCombo.SelectedItem = defaultRender ?? (renders.Count > 0 ? renders[0] : null);

            MicCombo.IsEnabled = true;
            CableCombo.IsEnabled = true;
            MonitorCombo.IsEnabled = true;
        }

        // ---------------- meters + spectrum ----------------

        private void UpdateMeters()
        {
            InputPeakText.Text = FormatDb(_engine.InputPeak);
            OutputPeakText.Text = FormatDb(_engine.OutputPeak);
            Spectrum.UpdateSpectrum(_engine.GetSpectrumSnapshot());
        }

        private static string FormatDb(float linearPeak)
        {
            if (linearPeak <= 0.00001f) return "-\u221e dB";
            double db = 20 * Math.Log10(linearPeak);
            return db.ToString("0.0") + " dB";
        }

        // ---------------- transport ----------------

        private void StartStopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_engine.IsRunning)
            {
                _engine.Stop();
                StartStopButton.Content = "START";
                StatusText.Text = "STOPPED";
            }
            else
            {
                _engine.MicDevice = (MicCombo.SelectedItem as AudioDeviceItem)?.Device;
                _engine.CableRenderDevice = (CableCombo.SelectedItem as AudioDeviceItem)?.Device;
                if (MonitorToggle.IsChecked == true)
                {
                    _engine.MonitorRenderDevice = (MonitorCombo.SelectedItem as AudioDeviceItem)?.Device;
                    _engine.SetMonitoring(true, _engine.MonitorRenderDevice);
                }

                _engine.Start();
                if (_engine.IsRunning)
                {
                    StartStopButton.Content = "STOP";
                    StatusText.Text = "RUNNING \u2014 ROUTED TO VIRTUAL CABLE";
                }
            }
        }

        private void MonitorToggle_Click(object sender, RoutedEventArgs e)
        {
            bool enabled = MonitorToggle.IsChecked == true;
            _engine.SetMonitoring(enabled, (MonitorCombo.SelectedItem as AudioDeviceItem)?.Device);
            StatusText.Text = _engine.IsRunning
                ? (enabled ? "RUNNING \u2014 MONITORING ON" : "RUNNING \u2014 ROUTED TO VIRTUAL CABLE")
                : StatusText.Text;
        }

        // ---------------- add-effect / presets menus ----------------

        private void AddEffectButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = (ContextMenu)FindResource("AddEffectMenu");
            menu.PlacementTarget = AddEffectButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void PresetsButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = (ContextMenu)FindResource("PresetsMenu");
            menu.PlacementTarget = PresetsButton;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        // ---------------- chain management ----------------

        private void AddGain_Click(object sender, RoutedEventArgs e) => _engine.AddEffect(new GainEffect());
        private void AddEq_Click(object sender, RoutedEventArgs e) => _engine.AddEffect(new EqualizerEffect());
        private void AddCompressor_Click(object sender, RoutedEventArgs e) => _engine.AddEffect(new CompressorEffect());
        private void AddHpf_Click(object sender, RoutedEventArgs e) => _engine.AddEffect(new HighPassFilterEffect());
        private void AddAgc_Click(object sender, RoutedEventArgs e) => _engine.AddEffect(new AutoGainControlEffect());
        private void AddNoise_Click(object sender, RoutedEventArgs e) => _engine.AddEffect(new NoiseSuppressorEffect());
        private void AddGate_Click(object sender, RoutedEventArgs e) => _engine.AddEffect(new ExpanderGateEffect());
        private void AddDeEsser_Click(object sender, RoutedEventArgs e) => _engine.AddEffect(new DeEsserEffect());
        private void AddLimiter_Click(object sender, RoutedEventArgs e) => _engine.AddEffect(new LimiterEffect());
        private void AddAec_Click(object sender, RoutedEventArgs e) => _engine.AddEffect(new AecEffect());

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is IAudioEffect fx)
                _engine.RemoveEffect(fx);
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is IAudioEffect fx)
            {
                int idx = _engine.Chain.IndexOf(fx);
                if (idx > 0) _engine.MoveEffect(idx, idx - 1);
            }
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is IAudioEffect fx)
            {
                int idx = _engine.Chain.IndexOf(fx);
                if (idx >= 0 && idx < _engine.Chain.Count - 1) _engine.MoveEffect(idx, idx + 1);
            }
        }

        // ---------------- presets ----------------

        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "MicFX Preset (*.micfx.json)|*.micfx.json|All files (*.*)|*.*",
                FileName = "MyPreset.micfx.json"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
                string json = PresetService.Serialize(name, _engine.Chain);
                System.IO.File.WriteAllText(dlg.FileName, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not save preset: " + ex.Message, "MicFX", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LoadPreset_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "MicFX Preset (*.micfx.json)|*.micfx.json|All files (*.*)|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                string json = System.IO.File.ReadAllText(dlg.FileName);
                var effects = PresetService.Deserialize(json);
                if (effects.Count == 0)
                {
                    MessageBox.Show("That preset file didn't contain any recognizable effects.", "MicFX", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                ReplaceChain(effects);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not load preset: " + ex.Message, "MicFX", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LoadNoiseSuppressionPreset_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This replaces your current effect chain with the recommended noise-suppression chain " +
                "(High-Pass \u2192 Noise Suppressor \u2192 AGC \u2192 Gate [disabled] \u2192 De-Esser \u2192 Compressor \u2192 Limiter). Continue?",
                "Load Noise Suppression Preset", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            ReplaceChain(PresetService.BuildNoiseSuppressionPreset());
        }

        private void ReplaceChain(List<IAudioEffect> newEffects)
        {
            _engine.ReplaceEffects(newEffects);
        }
    }
}

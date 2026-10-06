using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using MicFXApp.Audio.Effects;

namespace MicFXApp.Audio
{
    public class PresetEffectEntry
    {
        public string Type { get; set; } = "";
        public bool Enabled { get; set; }
        public Dictionary<string, double> Parameters { get; set; } = new();
    }

    public class PresetFile
    {
        public string Name { get; set; } = "";
        public List<PresetEffectEntry> Effects { get; set; } = new();
    }

    /// <summary>
    /// Serializes/deserializes the effect chain to/from JSON. Effect instances
    /// are reconstructed by a short type-name key (not the full CLR type), so
    /// preset files stay stable even if internal namespaces change later.
    /// </summary>
    public static class PresetService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private static readonly Dictionary<string, Func<IAudioEffect>> Factory = new()
        {
            ["Gain"] = () => new GainEffect(),
            ["Equalizer"] = () => new EqualizerEffect(),
            ["Compressor"] = () => new CompressorEffect(),
            ["HighPassFilter"] = () => new HighPassFilterEffect(),
            ["AutoGainControl"] = () => new AutoGainControlEffect(),
            ["NoiseSuppressor"] = () => new NoiseSuppressorEffect(),
            ["ExpanderGate"] = () => new ExpanderGateEffect(),
            ["DeEsser"] = () => new DeEsserEffect(),
            ["Limiter"] = () => new LimiterEffect(),
            ["Aec"] = () => new AecEffect(),
        };

        /// <summary>Maps a live effect instance back to its factory key.</summary>
        public static string KeyFor(IAudioEffect effect) => effect switch
        {
            GainEffect => "Gain",
            EqualizerEffect => "Equalizer",
            CompressorEffect => "Compressor",
            HighPassFilterEffect => "HighPassFilter",
            AutoGainControlEffect => "AutoGainControl",
            NoiseSuppressorEffect => "NoiseSuppressor",
            ExpanderGateEffect => "ExpanderGate",
            DeEsserEffect => "DeEsser",
            LimiterEffect => "Limiter",
            AecEffect => "Aec",
            _ => throw new NotSupportedException($"Unknown effect type: {effect.GetType().Name}"),
        };

        public static string Serialize(string presetName, ObservableCollection<IAudioEffect> chain)
        {
            var file = new PresetFile { Name = presetName };
            foreach (var fx in chain)
            {
                file.Effects.Add(new PresetEffectEntry
                {
                    Type = KeyFor(fx),
                    Enabled = fx.Enabled,
                    Parameters = fx.GetParameters(),
                });
            }
            return JsonSerializer.Serialize(file, JsonOptions);
        }

        /// <summary>Reconstructs a fresh, ordered list of effect instances from a preset file's JSON.</summary>
        public static List<IAudioEffect> Deserialize(string json)
        {
            var file = JsonSerializer.Deserialize<PresetFile>(json, JsonOptions)
                       ?? throw new InvalidOperationException("Preset file is empty or invalid.");

            var result = new List<IAudioEffect>();
            foreach (var entry in file.Effects)
            {
                if (!Factory.TryGetValue(entry.Type, out var create))
                    continue; // skip unknown effect types (e.g. preset from a newer app version) rather than failing the whole load

                var effect = create();
                effect.Enabled = entry.Enabled;
                effect.SetParameters(entry.Parameters);
                result.Add(effect);
            }
            return result;
        }

        /// <summary>Built-in starting point matching the recommended noise-suppression chain layout.</summary>
        public static List<IAudioEffect> BuildNoiseSuppressionPreset()
        {
            var hpf = new HighPassFilterEffect();
            hpf.CutoffHz = 90;

            var agc = new AutoGainControlEffect();
            agc.TargetDb = -18;
            agc.MaxBoostDb = 15;
            agc.ResponseMs = 600;

            var ns = new NoiseSuppressorEffect();
            ns.Amount = 60;

            var gate = new ExpanderGateEffect();
            // Start voice-first: the optional gate can clip quiet consonants.
            gate.Enabled = false;
            gate.ThresholdDb = -45;
            gate.Ratio = 4;
            gate.RangeDb = 25;

            var deesser = new DeEsserEffect();
            deesser.FrequencyHz = 6000;
            deesser.ThresholdDb = -24;
            deesser.Ratio = 4;

            var comp = new CompressorEffect();
            comp.ThresholdDb = -20;
            comp.Ratio = 3;
            comp.AttackMs = 10;
            comp.ReleaseMs = 150;
            comp.MakeupDb = 4;

            var limiter = new LimiterEffect();
            limiter.CeilingDb = -1;
            limiter.ReleaseMs = 80;

            // AEC deliberately left out of this default preset (and stays off by default when
            // added manually) - it's only useful if the mic can hear your own speakers, and
            // needlessly spending CPU/adding risk of artifacts otherwise.
            return new List<IAudioEffect> { hpf, ns, agc, gate, deesser, comp, limiter };
        }
    }
}

using MicFXApp.Audio.Effects;

internal static class NoiseSuppressorTests
{
    static int Main(string[] args)
    {
        if (args.Contains("--missing-native"))
        {
            System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(typeof(RNNoise.NET.Denoiser).Assembly,
                (_,_,_) => throw new DllNotFoundException("Injected missing native runtime"));
            var effect = new NoiseSuppressorEffect();
            try { effect.Prepare(2,44100); return 2; }
            catch (DllNotFoundException) { effect.Dispose(); effect.Dispose(); return 0; }
        }
        int failed = 0, passed = 0, skipped = 0;
        foreach (var test in new Action[] { AmountZeroUnity, PartitionInvariant, SampleRates, NativeDelay, StereoIsolation, ResetAndFormat, ToggleContinuity, ParametersAndLifetime, StartupFailure, FiniteAndAllocation, NoiseReduction, ResamplerAntiAlias, SpeechFixture, KeyboardFixture })
        {
            if (test == SpeechFixture && Environment.GetEnvironmentVariable("MICFX_SPEECH_F32") == null)
            {
                skipped++; Console.WriteLine("SKIP SpeechFixture: set MICFX_SPEECH_F32 to external 48kHz mono f32le speech"); continue;
            }
            if (test == KeyboardFixture && (Environment.GetEnvironmentVariable("MICFX_SPEECH_F32") == null || Environment.GetEnvironmentVariable("MICFX_KEYBOARD_F32") == null))
            {
                skipped++; Console.WriteLine("SKIP KeyboardFixture: set MICFX_SPEECH_F32 and MICFX_KEYBOARD_F32 to external 48kHz mono f32le recordings"); continue;
            }
            try { test(); passed++; Console.WriteLine($"PASS {test.Method.Name}"); }
            catch (Exception ex) { failed++; Console.WriteLine($"FAIL {test.Method.Name}: {ex.Message}"); }
        }
        Console.WriteLine($"Results: {passed} passed, {failed} failed, {skipped} skipped");
        return failed == 0 ? 0 : 1;
    }
    static float[] Signal(int count) => Enumerable.Range(0, count).Select(i => (float)(.15 * Math.Sin(i * .083) + .1 * Math.Sin(i * .019))).ToArray();
    static float[] Render(float[] input, int block, double amount = 100, int rate = 48000, int channels = 1)
    {
        var effect = new NoiseSuppressorEffect { Amount = amount };
        var output = new float[input.Length];
        for (int i = 0; i < input.Length; i += block * channels)
        {
            var b = input.Skip(i).Take(block * channels).ToArray(); effect.Process(b, channels, rate); b.CopyTo(output, i);
        }
        (effect as IDisposable)?.Dispose();
        return output;
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void AmountZeroUnity()
    {
        var input = Signal(12000); var output = Render(input, 137, 0);
        int delay = Enumerable.Range(0, 3000).OrderBy(d => Enumerable.Range(3000, 3000).Sum(i => Math.Abs(output[i] - input[i-d]))).First();
        double error = Enumerable.Range(3000, 9000).Max(i => Math.Abs(output[i] - input[i-delay]));
        Require(error < 1e-6, $"Amount=0 is not delayed unity: delay={delay}, error={error}");
    }
    static void SampleRates()
    {
        foreach (int rate in new[] { 8001, 12345, 44100, 47999, 48000, 48001, 96000, 191999 })
        {
            var input = Signal(24000);
            var a = Render(input, 17, 100, rate); var b = Render(input, 1024, 100, rate);
            Require(a.Zip(b, (x,y) => Math.Abs(x-y)).Max() < 1e-6, $"partition {rate}");
            using var dry = new NoiseSuppressorEffect { Amount = 0 }; dry.Prepare(1, rate);
            var output = Render(input, 37, 0, rate);
            Require(Enumerable.Range(dry.LatencySamples, input.Length-dry.LatencySamples).All(i => output[i] == input[i-dry.LatencySamples]), $"dry unity {rate}");
            Require(a.Any(x => Math.Abs(x) > .001), $"silent wet {rate}");
        }
    }
    static void NativeDelay()
    {
        using var native = new RNNoise.NET.Denoiser();
        var all = new float[480*8]; all[1060] = .7f;
        for (int i = 0; i < all.Length; i += 480) native.Denoise(all.AsSpan(i,480), false);
        int peak = Enumerable.Range(0, all.Length).MaxBy(i => Math.Abs(all[i]));
        Require(peak-1060 == RnNoiseProcessor.NativeDelay, $"native impulse delay={peak-1060}");
        foreach (int rate in new[] { 8000, 22050, 44100, 48000, 88200, 96000, 192000 })
        {
            var impulse = new float[rate/4]; impulse[rate/16] = .7f;
            var output = Render(impulse, 137, 100, rate);
            using var effect = new NoiseSuppressorEffect(); effect.Prepare(1,rate);
            int actual = Enumerable.Range(0, output.Length).MaxBy(i => Math.Abs(output[i]))-rate/16;
            Require(Math.Abs(actual-effect.LatencySamples) <= 1, $"alignment {rate}: {actual} != {effect.LatencySamples}");
        }
    }
    static void StereoIsolation()
    {
        var left = Signal(12000); var random = new Random(12);
        var right = left.Select(_ => (float)(random.NextDouble()*.1-.05)).ToArray();
        foreach (int rate in new[] {44100,48000,96000})
        {
            var stereo = left.Zip(right, (l,r) => new[] {l,r}).SelectMany(x=>x).ToArray();
            var output = Render(stereo, 317, 100, rate, 2);
            var lout = Render(left, 29, 100, rate); var rout = Render(right, 401, 100, rate);
            Require(Enumerable.Range(0,left.Length).All(i=>output[2*i]==lout[i] && output[2*i+1]==rout[i]), $"channel coupling {rate}");
        }
    }
    static void ResetAndFormat()
    {
        using var effect = new NoiseSuppressorEffect { Amount = 100 };
        effect.Process(Signal(4301),1,44100); effect.Reset();
        var silence = new float[6000]; effect.Process(silence,1,44100);
        Require(silence.All(x=>x==0), "Reset retained old audio");
        effect.Process(Signal(4301),1,44100);
        var fresh = Signal(12000); var changed = (float[])fresh.Clone();
        effect.Process(changed,1,96000);
        Require(changed.SequenceEqual(Render(fresh,12000,100,96000)), "format change retained state");
    }
    static void ToggleContinuity()
    {
        foreach (int rate in new[] {44100,48000,96000})
        {
            using var toggled = new NoiseSuppressorEffect { Amount=100 };
            using var reference = new NoiseSuppressorEffect { Amount=100 };
            var input = Signal(rate);
            for (int i=0;i<input.Length;i+=100)
            {
                toggled.Enabled = i < rate/4 || i >= rate/2;
                var a=input.Skip(i).Take(100).ToArray(); var b=(float[])a.Clone();
                toggled.Process(a,1,rate); reference.Process(b,1,rate);
                if(i>rate/2+rate/50) Require(a.SequenceEqual(b), "bypassed inference went stale");
                if(i>rate/4+rate/50 && i+100<rate/2)
                    Require(Enumerable.Range(0,a.Length).All(j=>a[j]==input[i+j-toggled.LatencySamples]), "bypass changed delay or unity");
            }
        }
    }
    static void ParametersAndLifetime()
    {
        var effect = new NoiseSuppressorEffect();
        Require(effect.Amount==60, "default changed");
        foreach(double value in new[] {double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        { effect.Amount=value; Require(double.IsFinite(effect.Amount), "nonfinite Amount"); }
        effect.SetParameters(new() { ["Amount"]=130 }); Require(effect.Amount==100,"clamp");
        try { effect.Prepare(1,0); throw new Exception("invalid format accepted"); } catch(ArgumentOutOfRangeException) {}
        effect.Prepare(1,48000); effect.Process(new float[480],1,48000);
        effect.Dispose(); effect.Dispose();
        try { effect.Prepare(1,48000); throw new Exception("disposed instance accepted"); } catch(ObjectDisposedException) {}
    }
    static void StartupFailure()
    {
        var start = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet")
            start.ArgumentList.Add(typeof(NoiseSuppressorTests).Assembly.Location);
        start.ArgumentList.Add("--missing-native");
        using var process = System.Diagnostics.Process.Start(start)!;
        if (!process.WaitForExit(30000)) { process.Kill(true); process.WaitForExit(); throw new Exception("startup failure probe timed out"); }
        Require(process.ExitCode == 0, "native load failure did not propagate safely");
    }
    static void FiniteAndAllocation()
    {
        foreach(int rate in new[]{44100,48000,96000})
        {
            using var effect=new NoiseSuppressorEffect(); effect.Prepare(1,rate);
            var bad=new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,float.MaxValue,-float.MaxValue};
            effect.Process(bad,1,rate);
            var block=new float[480];
            for(int i=0;i<40;i++) { effect.Process(block,1,rate); Require(block.All(float.IsFinite), "nonfinite output"); Array.Clear(block); }
            long before=GC.GetAllocatedBytesForCurrentThread();
            for(int i=0;i<100;i++) effect.Process(block,1,rate);
            long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
            Require(bytes==0,$"RT managed allocations {rate}: {bytes}");
        }
    }
    static double Energy(IEnumerable<float> values) => values.Select(x=>(double)x*x).Average();
    static void NoiseReduction()
    {
        var random=new Random(42); var input=Enumerable.Range(0,144000).Select(_=>(float)(.1*(random.NextDouble()*2-1))).ToArray();
        var output=Render(input,113,100);
        double db=10*Math.Log10(Energy(output.Skip(48000))/Energy(input.Skip(48000)));
        Console.WriteLine($"  stationary-noise attenuation: {db:F2} dB");
        Require(db < -8, "insufficient noise reduction");
    }
    static void ResamplerAntiAlias()
    {
        double Measure(int frequency)
        {
            var resampler=new StreamingResampler(96000,48000,64); var output=new List<float>();
            for(int i=0;i<12000;i++)
            { resampler.Push((float)Math.Sin(2*Math.PI*frequency*i/96000)); while(resampler.TryRead(out var value)) output.Add(value); }
            return Energy(output.Skip(1000));
        }
        double db=10*Math.Log10(Measure(30000)/Measure(1000));
        Console.WriteLine($"  96→48k alias rejection: {db:F2} dB"); Require(db < -50,"resampler aliases ultrasonic energy");
    }
    static void SpeechFixture()
    {
        // Optional real speech: raw mono normalized f32le at 48kHz, external to source tree.
        string? path=Environment.GetEnvironmentVariable("MICFX_SPEECH_F32");
        if(path==null) { Console.WriteLine("  SKIP real speech (set MICFX_SPEECH_F32 to 48kHz mono f32le)"); return; }
        byte[] bytes=File.ReadAllBytes(path); var speech=new float[bytes.Length/4]; Buffer.BlockCopy(bytes,0,speech,0,bytes.Length);
        var input=new float[speech.Length+960]; speech.CopyTo(input,0);
        var output=Render(input,173,100); var aligned=output.Skip(960).Take(speech.Length).ToArray();
        double dot=speech.Zip(aligned,(x,y)=>(double)x*y).Sum();
        double corr=dot/Math.Sqrt(speech.Sum(x=>(double)x*x)*aligned.Sum(x=>(double)x*x));
        double gain=10*Math.Log10(Energy(aligned)/Energy(speech));
        Console.WriteLine($"  real clean speech correlation={corr:F3}, energy change={gain:F2} dB");
        Require(corr>.75 && gain > -6 && gain < 3,"clean speech excessively altered");
        var random = new Random(7);
        var noisy = speech.Select(x => x + (float)(.06*(random.NextDouble()*2-1))).Concat(new float[960]).ToArray();
        var cleaned = Render(noisy, 317, 100).Skip(960).Take(speech.Length).ToArray();
        double before = 10*Math.Log10(Energy(speech)/Energy(speech.Zip(noisy,(x,y)=>x-y)));
        double after = 10*Math.Log10(Energy(speech)/Energy(speech.Zip(cleaned,(x,y)=>x-y)));
        Console.WriteLine($"  real speech + seeded white noise SNR: {before:F2} → {after:F2} dB");
        Require(after > before, "noisy speech SNR did not improve");
    }
    static void KeyboardFixture()
    {
        static float[] Load(string variable)
        {
            var bytes = File.ReadAllBytes(Environment.GetEnvironmentVariable(variable)!);
            Require(bytes.Length > 0 && bytes.Length % 4 == 0, "invalid raw fixture");
            var samples = new float[bytes.Length / 4];
            Buffer.BlockCopy(bytes, 0, samples, 0, bytes.Length);
            Require(samples.All(float.IsFinite) && Energy(samples) > 0, "invalid fixture samples");
            return samples;
        }
        var speech = Load("MICFX_SPEECH_F32");
        var keys = Load("MICFX_KEYBOARD_F32");
        var noise = Enumerable.Range(0, speech.Length).Select(i => keys[i % keys.Length]).ToArray();
        double scale = Math.Sqrt(Energy(speech) / (Energy(noise) * 10)); // 10 dB input SNR
        noise = noise.Select(x => (float)(x * scale)).ToArray();
        foreach (double amount in new[] { 60d, 100d })
        {
            var mixture = speech.Zip(noise, (x, y) => x + y).Concat(new float[960]).ToArray();
            var cleaned = Render(mixture, 317, amount).Skip(960).Take(speech.Length).ToArray();
            var noiseOnly = Render(noise.Concat(new float[960]).ToArray(), 113, amount).Skip(960).Take(noise.Length).ToArray();
            double attenuation = 10 * Math.Log10(Energy(noiseOnly) / Energy(noise));
            double snr = 10 * Math.Log10(Energy(speech) / Energy(speech.Zip(cleaned, (x, y) => x - y)));
            Console.WriteLine($"  recorded typing Amount={amount}: noise-only {attenuation:F2} dB; speech+typing SNR 10.00 → {snr:F2} dB");
            Require(attenuation < -3 && snr > 10, "recorded typing did not improve at this strength");
        }
    }
    static void PartitionInvariant()
    {
        var input = Signal(15000); var a = Render(input, 1); var b = Render(input, 2048);
        double error = a.Zip(b, (x,y) => Math.Abs(x-y)).Max();
        Require(error < 1e-6, $"callback-dependent output: max error={error}");
    }
}

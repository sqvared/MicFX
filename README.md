# MicFX — Real-time Mic Gain / EQ / Compressor for Windows

## What's new

- **7 new effect stages** for a full noise-suppression chain: High-Pass Filter, Auto Gain
  Control, adaptive Noise Suppressor (classical spectral subtraction, not ML), Expander/Gate,
  De-Esser, Limiter, and an experimental Echo Canceller (AEC).
- **Presets** — save/load your chain to a `.json` file, plus a one-click built-in
  "Noise Suppression Chain" preset (Chain menu → PRESETS).
- **Live spectrum analyzer** replacing the old flat level bars — see your mic's actual
  frequency content in real time, FL Studio EQ2-style.
- **Bug fixes**: instant device dropdowns (were briefly laggy), a monitor on/off toggle that
  no longer risks breaking the audio pipeline, a sleek custom scrollbar, and fixed clipped
  icons on the reorder/remove buttons.

A Windows desktop app (WPF, .NET 8) that captures your physical microphone, runs it
through an editable effects chain (Gain, 7-band Equalizer, Compressor), and outputs
the processed signal so other apps (Zoom, Discord, OBS, Teams, etc.) can use it as
their microphone.

## Important: about the "virtual microphone" part

Windows does not allow a normal application to register itself as a system audio
device — that requires a **signed kernel-mode audio driver**, built with the Windows
Driver Kit and signed through Microsoft's hardware dev center. That's out of reach for
an app-level project (and is how VoiceMeeter, Krisp, NVIDIA Broadcast, etc. all work
under the hood too — they all bundle or depend on a virtual-cable driver).

The practical, reliable solution — and what this app is built around — is:

1. Install a free virtual audio cable driver, e.g. **VB-Audio Virtual Cable**:
   https://vb-audio.com/Cable/ (adds a device pair: "CABLE Input" (playback) and
   "CABLE Output" (recording)).
2. This app captures your real mic, processes it, and plays the result into
   **CABLE Input**.
3. In Zoom/Discord/OBS/etc., set your microphone to **CABLE Output** — that's the
   processed signal.

Other virtual-cable options (e.g. VoiceMeeter's own virtual inputs) work the same way;
just point "Virtual cable output" at whichever playback endpoint your chosen driver exposes.

## Features

- **Gain** — clean linear gain, -24 to +24 dB, with soft clipping to avoid harsh digital clipping.
- **Equalizer** — 7 bands (60Hz/150Hz/400Hz/1kHz/2.5kHz/6kHz/12kHz), low/high shelving on
  the outer bands and peaking (bell) filters in the middle, each with its own vertical
  slider for a mixer-style look.
- **Compressor** — threshold, ratio, attack, release, and makeup gain, using a linked
  (mono-summed) peak detector so stereo signals don't shift when gain reduction kicks in.
- **Chain management** — add/remove any number of effects, mute individual effects with
  the checkbox, and reorder with the ▲/▼ buttons. Effects process top-to-bottom.
- **Live meters** — input and output peak meters update ~25x/sec.
- **Test / Monitor toggle** — routes the processed signal to a second output device
  (e.g. your speakers/headphones) simultaneously, so you can hear exactly what other
  apps will receive, without needing to open a call.

## Building & running

Requirements: Windows 10/11, [.NET 8 SDK](https://dotnet.microsoft.com/download), and
(recommended) Visual Studio 2022 with the ".NET desktop development" workload.

**Option A — Visual Studio**
1. Open `MicFXApp/MicFXApp.csproj` in Visual Studio.
2. Set configuration to `Debug` / `x64`, hit Run (F5).

**Option B — command line**
```
cd MicFXApp
dotnet build
dotnet run --project MicFXApp
```
NAudio restores automatically as a NuGet package on first build.

## Usage

1. Install VB-Audio Virtual Cable (or another virtual cable driver) and reboot if prompted.
2. Launch MicFX.
3. Pick your real microphone under **Microphone (input)**.
4. Pick **CABLE Input (VB-Audio Virtual Cable)** under **Virtual cable output** (the app
   tries to auto-select this if it finds a device with "CABLE" in its name).
5. Pick your speakers/headphones under **Monitor / test output**.
6. Build your chain: use the **+ Gain / + Equalizer / + Compressor** buttons, drag levels
   with the sliders, mute a stage with its checkbox, reorder with ▲/▼, or remove with ✕.
7. Click **Test / Monitor** to hear the processed chain live through your speakers while
   you dial things in.
8. Click **Start** to begin routing into the virtual cable. In your call/recording app,
   select **CABLE Output** as the microphone.

## Building the installer

There's now an `installer/setup.iss` script ([Inno Setup](https://jrsoftware.org/isinfo.php))
that produces a single `MicFX-Setup.exe`. It:

- Installs MicFX to Program Files with Start Menu / optional desktop shortcuts and a
  proper uninstall entry.
- Has a checkbox (checked by default) that, at install time, **downloads the official
  VB-Audio Virtual Cable installer directly from vb-audio.com** and launches it for the
  user. It does *not* bundle VB-Audio's binary inside our installer — VB-Audio's own
  licensing terms (https://vb-audio.com/Services/licensing.htm) ask that distribution/
  integration deals be arranged with them directly, so downloading fresh from their site
  at install time (same as if the user did it themselves) is the correct approach rather
  than silently redistributing their driver.
- Skips that step automatically if it detects VB-CABLE is already installed.
- Reminds the user to reboot after the VB-CABLE installer finishes (required for the new
  audio driver to appear).

**Steps to produce `MicFX-Setup.exe`** (all on Windows):

1. Publish a self-contained, single-file build of the app:
   ```
   cd MicFXApp
   dotnet publish -c Release -r win-x64 --self-contained true ^
     -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
   ```
   This produces `MicFXApp\bin\Release\net8.0-windows\win-x64\publish\MicFXApp.exe`
   (no separate .NET runtime install needed on the target machine).
2. Install [Inno Setup 6](https://jrsoftware.org/isdl.php).
3. Open `installer/setup.iss` in the Inno Setup Compiler (or run
   `ISCC.exe installer\setup.iss` from the command line) and build.
4. The finished installer lands at `installer/Output/MicFX-Setup.exe`.

Notes:
- The VB-CABLE download step requires internet access at install time and calls
  PowerShell's `Invoke-WebRequest`/`Expand-Archive` under the hood — no extra Inno
  plugins needed. If vb-audio.com is unreachable, the installer just tells the user to
  grab it manually from https://vb-audio.com/Cable/ and continues.
- VB-Audio periodically revises their driver pack filename/version (currently
  `VBCABLE_Driver_Pack45.zip` at `https://download.vb-audio.com/Download_CABLE/`);
  if their download URL changes again, update
  `CableDownloadUrl` at the top of the `[Code]` section in `setup.iss`.
- If you'd rather not auto-download anything, just untick "Install VB-Audio Virtual
  Cable" in the installer — MicFX still installs fine, you'd just need to grab the
  cable driver yourself first.

## Notes / things you may want to extend

- Sample rate / format follow whatever `WasapiCapture` reports for your mic (usually
  48kHz float). All DSP is written generically against channel count and sample rate,
  so it'll adapt automatically.
- Presets (save/load chain configurations to JSON) would be a natural next step —
  the effect classes already expose plain bindable properties, so serializing
  `Chain` to JSON and reconstructing effect instances by type name is straightforward to add.
- A "reset to default chain" button, or per-effect enable animations, could be added
  to `MainWindow.xaml.cs` without touching the DSP code.

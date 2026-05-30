# WoburnAutoEQ

WoburnAutoEQ is a local Windows desktop app for Marshall Woburn 3 speakers connected to a PC through RCA. It captures system audio with WASAPI loopback, analyzes the sound every 5 seconds, chooses a rule-based EQ preset, and writes that preset to Equalizer APO.

The app is designed for a clean, warm, vocal-forward sound with tight bass and less boom. It does not use machine learning, cloud AI, uploads, or external APIs. Audio analysis happens locally on the user's PC.

## Open-source dependencies

- .NET 8: runtime and build SDK for the Windows app.
- WPF: Windows desktop UI framework included with .NET.
- NAudio: open-source WASAPI loopback audio capture.
- MathNet.Numerics: open-source FFT and numerical DSP utilities.
- Equalizer APO: open-source system-wide audio EQ backend.

Hardcodet.NotifyIcon.Wpf is not used in this first version. System tray support is a TODO so the main app stays simple and stable.

## How it works

```text
YouTube / Chrome / system audio
      -> WASAPI loopback capture with NAudio
      -> DSP Analyzer
      -> Preset Engine for Marshall Woburn 3
      -> writes woburn_autoeq.txt
      -> Equalizer APO applies EQ system-wide
```

## Run in development

```bat
run_dev.bat
```

## Native WASAPI analyzer for X570 AORUS ULTRA

The repo also includes an independent C++17 WASAPI module at:

```text
native\wasapi_x570_autoeq\main.cpp
```

It is designed for Gigabyte X570 AORUS ULTRA + Realtek ALC1220-VB and uses:

- `AUDCLNT_SHAREMODE_SHARED` so other apps can keep playing audio.
- `AUDCLNT_STREAMFLAGS_LOOPBACK` to capture the default render endpoint after Windows audio engine post-processing.
- `AUDCLNT_STREAMFLAGS_EVENTCALLBACK` for event-driven capture instead of polling.
- 48 kHz, stereo, 32-bit IEEE float `WAVEFORMATEXTENSIBLE` when supported, with fallback to the endpoint mix format.
- 4096-point manual FFT, Hann window, 50% overlap, 100 bands from 20 Hz to 20 kHz.

Recommended architecture:

```text
System audio
  -> Windows mixer / Equalizer APO / Realtek endpoint
  -> native WASAPI loopback analyzer
  -> JSON AutoEQ suggestion
  -> WPF app writes Equalizer APO preset
```

This avoids double audio, echo, and feedback. The native `--process` mode is included only for future routing such as:

```text
Apps -> Virtual Audio Cable -> native WASAPI process -> Realtek ALC1220-VB
```

Do not use `--process` on the same default Realtek endpoint unless you intentionally want a render test.

### Build native WASAPI module

GCC/MinGW:

```bat
g++ -std=c++17 native\wasapi_x570_autoeq\main.cpp -lole32 -luuid -lwinmm -o wasapi_x570_autoeq.exe
```

Visual Studio Developer Command Prompt:

```bat
cl /EHsc /std:c++17 native\wasapi_x570_autoeq\main.cpp /link ole32.lib uuid.lib winmm.lib /OUT:wasapi_x570_autoeq.exe
```

Run analyzer mode for 10 seconds:

```bat
wasapi_x570_autoeq.exe --analyze --seconds 10
```

Experimental process mode with separate routing:

```bat
wasapi_x570_autoeq.exe --process --input "Virtual Cable" --output "Realtek" --seconds 10
```

## Build portable folder

```bat
build.bat
```

After build, the portable folder is:

```text
F:\autoEQ\dist\WoburnAutoEQ
```

The executable is:

```text
F:\autoEQ\dist\WoburnAutoEQ\WoburnAutoEQ.exe
```

## Equalizer APO setup

Equalizer APO is expected at:

```text
C:\Program Files\EqualizerAPO\config
```

Run WoburnAutoEQ as Administrator the first time so it can add this line to `config.txt`:

```text
Include: woburn_autoeq.txt
```

Before modifying `config.txt`, the app creates:

```text
C:\Program Files\EqualizerAPO\config\config.txt.bak
```

The app does not delete or overwrite the user's existing Equalizer APO configuration. It writes only this generated file:

```text
C:\Program Files\EqualizerAPO\config\woburn_autoeq.txt
```

If the app cannot write to the Equalizer APO folder, it shows:

```text
Please run WoburnAutoEQ as Administrator once to connect with Equalizer APO.
```

## Verify Equalizer APO received the preset

1. Open `C:\Program Files\EqualizerAPO\config\config.txt`.
2. Check that it contains `Include: woburn_autoeq.txt`.
3. Open `C:\Program Files\EqualizerAPO\config\woburn_autoeq.txt`.
4. Check that it contains a preset such as `Preamp: -3 dB` and `Filter: ON PK ...` lines.

## Troubleshooting if EQ does not change

- Open Equalizer APO Configurator.
- Select the correct playback device used by Windows/Chrome/YouTube.
- Reboot Windows after changing the playback device in Configurator.
- Check that `config.txt` includes `Include: woburn_autoeq.txt`.
- Check that `woburn_autoeq.txt` contains the current preset.
- Run WoburnAutoEQ as Administrator once if the include line or EQ file cannot be written.

## Presets

- Woburn 3 - Clean Warm
- Woburn 3 - Near Wall
- Less Boom
- Clear Vocal
- Soft Treble
- Night Mode
- Bypass

## Privacy

WoburnAutoEQ does not upload audio. It does not call cloud AI or external APIs. All audio capture and DSP analysis run locally on the Windows machine.
# AutoEQ

AutoEQ is a local Windows desktop app for general output-device AutoEQ. It captures system audio with WASAPI loopback, analyzes sound every few seconds, builds a rule-based EQ curve from the active OutputAudioProfile, and writes that preset to Equalizer APO.

The app is designed for a clean, warm, vocal-forward sound with tight bass and less boom. It does not use machine learning, cloud AI, uploads, or external APIs. Audio analysis happens locally on the user's PC.

## Open-source dependencies

- .NET 8: runtime and build SDK for the Windows app.
- WPF: Windows desktop UI framework included with .NET.
- NAudio: open-source WASAPI loopback audio capture.
- MathNet.Numerics: open-source FFT and numerical DSP utilities.
- Hardcodet.NotifyIcon.Wpf: open-source WPF system tray support.
- Equalizer APO: open-source system-wide audio EQ backend.

System tray support uses Hardcodet.NotifyIcon.Wpf so the app can later expose quick actions such as Open AutoEQ, Bypass, Clean Warm, Night Mode, and Exit.

## Related free/open-source projects

- [AutoEq](https://github.com/jaakkopasanen/AutoEq) (MIT): reference for parametric EQ filters, target curves, and Equalizer APO export format. AutoEQ uses it as an algorithm/format reference only and does not import headphone datasets for the device-specific profile.
- [NAudio](https://github.com/naudio/NAudio) (MIT): WASAPI loopback capture dependency used by the Windows app.
- [Math.NET Numerics](https://github.com/mathnet/mathnet-numerics) (MIT): FFT and numerical DSP dependency used by the analyzer.
- [Hardcodet.NotifyIcon.Wpf](https://github.com/hardcodet/wpf-notifyicon) (MIT): WPF tray icon dependency for future tray actions.
- [EACS](https://github.com/psidex/EACS) (MIT): reference for safe Equalizer APO configuration switching. AutoEQ does not vendor its code.
- [Equalizer APO mirror](https://github.com/mirror/equalizerapo) (GPL-2.0): external backend reference. Users install Equalizer APO separately; AutoEQ does not embed or copy Equalizer APO code.

## How it works

```text
YouTube / Chrome / system audio
      -> WASAPI loopback capture with NAudio
      -> DSP Analyzer
      -> Preset Engine for active OutputAudioProfile
      -> writes AutoEQ_autoeq.txt
      -> Equalizer APO applies EQ system-wide
```

## Run in development

```bat
run_dev.bat
```

Use this while editing code. Stop the running app, save your files, then run `run_dev.bat` again to launch the latest source.

Editing `.cs` or `.xaml` files does not update an already-built `.exe`. The app must be restarted from source or rebuilt.

## Build and test

```bat
dotnet build AutoEQ.sln
dotnet test AutoEQ.sln --no-build
```

The test project uses xUnit and targets `net8.0-windows10.0.19041.0` so it can reference the WPF app while adding non-UI characterization tests around DSP and future preset-switching logic.

## Native WASAPI analyzer

The repo also includes an independent C++17 WASAPI module at:

```text
native\wasapi_autoeq\main.cpp
```

It is device-neutral; Gigabyte X570 AORUS ULTRA + selected output endpoint is only a tested reference setup. It uses:

- `AUDCLNT_SHAREMODE_SHARED` so other apps can keep playing audio.
- `AUDCLNT_STREAMFLAGS_LOOPBACK` to capture the default render endpoint after Windows audio engine post-processing.
- `AUDCLNT_STREAMFLAGS_EVENTCALLBACK` for event-driven capture instead of polling.
- 48 kHz, stereo, 32-bit IEEE float `WAVEFORMATEXTENSIBLE` when supported, with fallback to the endpoint mix format.
- 4096-point manual FFT, Hann window, 50% overlap, 100 bands from 20 Hz to 20 kHz.

Recommended architecture:

```text
System audio
  -> Windows mixer / Equalizer APO / selected output endpoint
  -> native WASAPI loopback analyzer
  -> JSON AutoEQ suggestion
  -> WPF app writes Equalizer APO preset
```

This avoids double audio, echo, and feedback. The native `--process` mode is included only for future routing such as:

```text
Apps -> Virtual Audio Cable -> native WASAPI process -> selected output endpoint
```

Do not use `--process` on the same default output endpoint unless you intentionally want a render test.

### Build native WASAPI module

GCC/MinGW:

```bat
g++ -std=c++17 native\wasapi_autoeq\main.cpp -lole32 -luuid -lwinmm -o wasapi_autoeq.exe
```

Visual Studio Developer Command Prompt:

```bat
cl /EHsc /std:c++17 native\wasapi_autoeq\main.cpp /link ole32.lib uuid.lib winmm.lib /OUT:wasapi_autoeq.exe
```

Run analyzer mode for 10 seconds:

```bat
wasapi_autoeq.exe --analyze --seconds 10
```

Experimental process mode with separate routing:

```bat
wasapi_autoeq.exe --process --input "Virtual Cable" --output "Realtek" --seconds 10
```

## Build portable folder

```bat
build.bat
```

Run `build.bat` every time you want the portable `.exe` to include your latest code changes. The script closes running `AutoEQ` processes, builds Release, publishes win-x64, clears the old `dist\AutoEQ` folder, and copies the new output there.

After build, the portable folder is:

```text
F:\autoEQ\dist\AutoEQ
```

The executable is:

```text
F:\autoEQ\dist\AutoEQ\AutoEQ.exe
```

If you run another copy of `AutoEQ.exe` from another folder, you may be launching an older build. Use the path above or the desktop shortcut recreated by `build.bat`.

## Equalizer APO setup

Equalizer APO is expected at:

```text
C:\Program Files\EqualizerAPO\config
```

Run AutoEQ as Administrator the first time so it can add this line to `config.txt`:

```text
Include: AutoEQ_autoeq.txt
```

Before modifying `config.txt`, the app creates:

```text
C:\Program Files\EqualizerAPO\config\config.txt.bak
```

The app does not delete or overwrite the user's existing Equalizer APO configuration. It writes only this generated file:

```text
C:\Program Files\EqualizerAPO\config\AutoEQ_autoeq.txt
```

If the app cannot write to the Equalizer APO folder, it shows:

```text
Please run AutoEQ as Administrator once to connect with Equalizer APO.
```

## Verify Equalizer APO received the preset

1. Open `C:\Program Files\EqualizerAPO\config\config.txt`.
2. Check that it contains `Include: AutoEQ_autoeq.txt`.
3. Open `C:\Program Files\EqualizerAPO\config\AutoEQ_autoeq.txt`.
4. Check that it contains a preset such as `Preamp: -3 dB` and `Filter: ON PK ...` lines.

## Troubleshooting if EQ does not change

- Open Equalizer APO Configurator.
- Select the correct playback device used by Windows/Chrome/YouTube.
- Reboot Windows after changing the playback device in Configurator.
- Check that `config.txt` includes `Include: AutoEQ_autoeq.txt`.
- Check that `AutoEQ_autoeq.txt` contains the current preset.
- Run AutoEQ as Administrator once if the include line or EQ file cannot be written.

## Presets

- AutoEQ 3 - Clean Warm
- AutoEQ 3 - Near Wall
- Less Boom
- Clear Vocal
- Soft Treble
- Night Mode
- Bypass

## Privacy

AutoEQ does not upload audio. It does not call cloud AI or external APIs. All audio capture and DSP analysis run locally on the Windows machine.

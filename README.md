<div align="center">
  <img src="docs/autoeq-logo.svg" width="96" height="96" alt="AutoEQ logo"/>
  <h1>AutoEQ</h1>
  <p>Local, real-time system-audio equalizer — no cloud, no uploads, no ML.</p>

  [![Release](https://img.shields.io/github/v/release/AlexsanderBo/AutoEQ?style=flat-square&color=3b82f6)](https://github.com/AlexsanderBo/AutoEQ/releases/latest)
  [![License: MIT](https://img.shields.io/badge/license-MIT-22c55e?style=flat-square)](LICENSE)
  [![.NET 8](https://img.shields.io/badge/.NET-8.0-7c3aed?style=flat-square)](https://dotnet.microsoft.com)
  [![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-0ea5e9?style=flat-square)](#install)
</div>

---

## What it does

AutoEQ captures system audio in real time, analyzes the frequency response, and writes a parametric EQ preset to a system-wide equalizer backend — tuned for a warm, vocal-forward sound with tight bass.

```
System audio
  ↓ WASAPI loopback (Windows) / parec monitor (Linux)
  ↓ DSP Analyzer  ─  FFT · Hann window · 100 bands · 20 Hz–20 kHz
  ↓ Preset Engine
  ↓ Equalizer APO preset (Windows)  /  PipeWire filter-chain (Linux)
  ↓ Applied system-wide — all apps hear the EQ
```

---

## Install

### Windows — `.msi` installer

| Step | Action |
|------|--------|
| 1 | Install **[Equalizer APO](https://sourceforge.net/projects/equalizerapo/)** (required backend) |
| 2 | Download `AutoEQ-0.1.0-win-x64.msi` from [Releases](https://github.com/AlexsanderBo/AutoEQ/releases/latest) |
| 3 | Double-click the `.msi` → Next → Install |
| 4 | Launch **AutoEQ** from the Start Menu |
| 5 | Run as Administrator once so it can write to the Equalizer APO config folder |

> **Tip:** After the first admin run, normal user mode is enough for everyday use.

### Linux — `.deb` package

| Step | Action |
|------|--------|
| 1 | Install deps: `sudo apt install pulseaudio-utils pipewire pipewire-pulse` |
| 2 | Download `autoeq-linux_0.1.0_amd64.deb` from [Releases](https://github.com/AlexsanderBo/AutoEQ/releases/latest) |
| 3 | `sudo dpkg -i autoeq-linux_0.1.0_amd64.deb` |
| 4 | `autoeq-linux --install-pipewire --install-startup --setup-only` |
| 5 | `systemctl --user restart pipewire pipewire-pulse` |
| 6 | Select **AutoEQ Sink** in Ubuntu sound settings |

---

## Presets

| Preset | Character |
|--------|-----------|
| **AutoEQ 3 — Clean Warm** | Balanced warmth, reference-leaning |
| **AutoEQ 3 — Near Wall** | Bass cut for desk / near-wall placement |
| **Less Boom** | Tighter, faster bass |
| **Clear Vocal** | Forward mids, speech clarity |
| **Soft Treble** | Reduced high-frequency fatigue |
| **Night Mode** | Low-volume loudness compensation |
| **Bypass** | Flat — disables all EQ processing |

---

## Platform support

| Feature | Windows | Linux |
|---------|:-------:|:-----:|
| Real-time audio capture | WASAPI loopback (NAudio) | `parec` PipeWire monitor |
| EQ backend | Equalizer APO | PipeWire filter-chain |
| GUI | Avalonia (Fluent) | — (CLI runner) |
| Autostart | Registry `HKCU\Run` | `~/.config/autostart/` |
| Installer | `.msi` (WiX 5) | `.deb` (dpkg) |
| Volume control | Windows Core Audio | `pactl` |

---

## Privacy

| What | Status |
|------|--------|
| Audio upload | ✗ Never |
| Cloud / AI API calls | ✗ Never |
| Internet access | ✗ None |
| Local processing only | ✓ Always |

---

## Build from source

### Prerequisites

| Tool | Version | Purpose |
|------|---------|---------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 8.0 | Build + publish |
| [WiX Toolset](https://wixtoolset.org) | 5.0.2 | Windows MSI |
| MSVC (`cl.exe`) | VS 2022 | Native WASAPI helper |
| `dpkg-deb` | any | Linux DEB (Linux only) |

Install WiX:
```powershell
dotnet tool install --global wix --version 5.0.2
```

### Run in development

```bat
run_dev.bat
```

### Build + test

```bat
dotnet build AutoEQ.sln
dotnet test  AutoEQ.sln --no-build
```

### Package

| Target | Command | Output |
|--------|---------|--------|
| Windows MSI | `.\scripts\windows\build-msi.ps1` | `dist\windows\AutoEQ-*-win-x64.msi` |
| Windows portable | `build.bat` | `dist\AutoEQ\AutoEQ.exe` |
| Linux `.deb` | `./scripts/linux/build-deb.sh` *(on Linux/WSL)* | `dist/linux/autoeq-linux_*_amd64.deb` |
| Linux binary | `build_linux.bat` | `dist\AutoEQ.Linux\autoeq-linux` |

Version is set once in `Directory.Build.props`:
```xml
<Version>0.1.0</Version>
```

---

## Architecture

```
AutoEQ.sln
├── AutoEQ/          Avalonia GUI — Windows desktop app
│   ├── Services/    PlatformServiceFactory, AudioCaptureService, EqualizerApoManager …
│   ├── ViewModels/  MainViewModel
│   └── Views/       MainWindow.axaml
├── AutoEQ.Core/     Shared DSP — PresetEngine, DynamicVoicing, MathNet FFT
├── AutoEQ.Linux/    Headless CLI runner — PipeWire EQ writer, parec capture
└── AutoEQ.Tests/    xUnit — PresetEngine, DynamicVoicing, EqualizerApoManager …
```

### Native WASAPI module

`native/wasapi_autoeq/main.cpp` — C++17 WASAPI loopback analyzer:

| Parameter | Value |
|-----------|-------|
| Sample rate | 48 kHz |
| Channels | Stereo |
| Format | 32-bit IEEE float |
| Share mode | `AUDCLNT_SHAREMODE_SHARED` |
| Capture | `AUDCLNT_STREAMFLAGS_LOOPBACK` |
| FFT size | 4096 points |
| Window | Hann, 50% overlap |
| Bands | 100 (20 Hz – 20 kHz) |

Build (from VS Developer Command Prompt):
```bat
native\wasapi_autoeq\build_native.bat
```

---

## Equalizer APO setup (Windows)

AutoEQ writes to:

```
C:\Program Files\EqualizerAPO\config\AutoEQ_autoeq.txt
```

And inserts this line into `config.txt` (once, on first admin run):

```
Include: AutoEQ_autoeq.txt
```

A backup is created at `config.txt.bak` before any modification. AutoEQ never deletes or overwrites user EQ configuration.

**Verify the preset was applied:**
1. Open `C:\Program Files\EqualizerAPO\config\config.txt` — confirm the `Include:` line is present.
2. Open `AutoEQ_autoeq.txt` — confirm it contains `Preamp:` and `Filter:` lines.

**Troubleshooting EQ not changing:**
- Open Equalizer APO Configurator → select the correct playback device.
- Reboot after changing the device in Configurator.
- Run AutoEQ as Administrator once if the include line cannot be written.

---

## Open-source dependencies

| Library | License | Purpose |
|---------|---------|---------|
| [Avalonia UI](https://github.com/AvaloniaUI/Avalonia) | MIT | Cross-platform desktop UI |
| [FluentAvalonia](https://github.com/amwx/FluentAvalonia) | MIT | Fluent-style controls |
| [NAudio](https://github.com/naudio/NAudio) | MIT | WASAPI loopback capture |
| [Math.NET Numerics](https://github.com/mathnet/mathnet-numerics) | MIT | FFT and DSP |
| [Equalizer APO](https://github.com/mirror/equalizerapo) | GPL-2.0 | Windows EQ backend (separate install) |
| [AutoEq](https://github.com/jaakkopasanen/AutoEq) | MIT | Algorithm/format reference |
| [EACS](https://github.com/psidex/EACS) | MIT | Safe APO config switching reference |

---

<div align="center">
  <sub>All audio analysis runs locally. No internet access required.</sub>
</div>

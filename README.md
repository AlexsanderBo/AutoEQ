<div align="center">
  <img src="docs/banner.svg" width="900" alt="AutoEQ banner"/>

  <br/>

  [![Release](https://img.shields.io/github/v/release/AlexsanderBo/AutoEQ?style=for-the-badge&color=3b82f6&logo=github&logoColor=white)](https://github.com/AlexsanderBo/AutoEQ/releases/latest)
  [![License: MIT](https://img.shields.io/badge/License-MIT-22c55e?style=for-the-badge&logo=opensourceinitiative&logoColor=white)](LICENSE)
  [![Platform](https://img.shields.io/badge/Windows%2010%2F11-0078D4?style=for-the-badge&logo=windows&logoColor=white)](#-windows-setup)
  [![Platform](https://img.shields.io/badge/Ubuntu%20%2F%20Debian-E95420?style=for-the-badge&logo=ubuntu&logoColor=white)](#-linux-setup)
</div>

<br/>

> **AutoEQ** is a system-wide equalizer that runs entirely on your machine — no cloud, no uploads, no telemetry.  
> Warm, vocal-forward sound tuned for long listening sessions and podcasts.

---

## 📥 Download

<div align="center">

| 🖥️ Platform | 📦 Installer | ✅ Requirements |
|:---:|:---:|:---|
| **Windows 10 / 11** | [**⬇ AutoEQ-0.1.0-win-x64.msi**](https://github.com/AlexsanderBo/AutoEQ/releases/latest/download/AutoEQ-0.1.0-win-x64.msi) | [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) must be installed first |
| **Ubuntu / Debian** | [**⬇ autoeq-linux_0.1.0_amd64.deb**](https://github.com/AlexsanderBo/AutoEQ/releases/latest) | `pulseaudio-utils` · `pipewire` · `pipewire-pulse` |

</div>

---

## 🪟 Windows Setup

<table>
<tr>
<td width="48px" align="center">1️⃣</td>
<td>Install <a href="https://sourceforge.net/projects/equalizerapo/"><strong>Equalizer APO</strong></a> — required; it hooks into the Windows audio pipeline. Reboot after installing.</td>
</tr>
<tr>
<td align="center">2️⃣</td>
<td>Run <code>AutoEQ-0.1.0-win-x64.msi</code> → Next → Install.</td>
</tr>
<tr>
<td align="center">3️⃣</td>
<td>Open <strong>AutoEQ</strong> from the Start Menu.</td>
</tr>
<tr>
<td align="center">4️⃣</td>
<td>First launch: <strong>Run as Administrator</strong> once so AutoEQ can register with Equalizer APO. Normal user mode is fine after that.</td>
</tr>
</table>

**Uninstall:**  
`Settings → Apps → Installed apps → AutoEQ → Uninstall`

---

## 🐧 Linux Setup

```bash
# 1. Install dependencies
sudo apt install pulseaudio-utils pipewire pipewire-pulse

# 2. Install the package
sudo dpkg -i autoeq-linux_0.1.0_amd64.deb

# 3. Initialize (run once)
autoeq-linux --install-pipewire --install-startup --setup-only

# 4. Restart audio services
systemctl --user restart pipewire pipewire-pulse
```

Then go to **Settings → Sound → Output** and select **AutoEQ Sink**.

> **Uninstall:** `sudo apt remove autoeq-linux`  
> Personal config at `~/.config/autoeq/` is preserved on uninstall.

---

## 🎛️ Presets

Each preset is a hand-tuned EQ curve for a specific use case:

<div align="center">

| Preset | 🎵 Sound | Best for |
|:---:|:---|:---|
| **Reference** | AutoEQ signature curve | Everyday listening, mix checking |
| **Cozy** | Warm, balanced | Acoustic, jazz, lo-fi |
| **Corner** | Bass cut | Speakers placed in a corner (bass buildup) |
| **Tight** | Punchy, fast bass | EDM, hip-hop |
| **Podcast** | Forward mids, clear vocals | Podcasts, audiobooks, calls |
| **Silk** | Reduced treble | Long sessions, ear fatigue |
| **Midnight** | Smooth at low volume | Late-night listening |
| **Flat** | No EQ | A/B testing, calibration |

</div>

---

## ✨ Features

<div align="center">

|  | Feature | Details |
|:---:|:---|:---|
| 🔒 | **Fully local** | Zero network — no pings, no telemetry, no cloud |
| ⚡ | **Real-time** | < 10 ms latency, system-wide processing |
| 🎨 | **8 presets** | Hand-tuned for distinct use cases |
| 🖥️ | **Cross-platform** | Windows 10/11 + Ubuntu / Debian |
| 🪶 | **Lightweight** | ~30 MB RAM, < 1% CPU at idle |
| 🔁 | **Auto-start** | Runs at login, no manual launch needed |

</div>

---

## 🏗️ How it works

```
┌─────────────────────────────────────────────────────┐
│                    AutoEQ App                       │
│  ┌──────────┐    ┌──────────┐    ┌───────────────┐  │
│  │  Preset  │───▶│ EQ Filter│───▶│  Config File  │  │
│  │  Picker  │    │ Generator│    │  (EqualizerAPO │  │
│  └──────────┘    └──────────┘    │  / PipeWire)  │  │
└─────────────────────────────────┴───────────────────┘
          │                               │
          ▼                               ▼
  ┌──────────────┐               ┌───────────────────┐
  │  Audio Hook  │               │   System Audio    │
  │ (APO / PW)   │◀──────────────│   All apps, all   │
  └──────────────┘               │   outputs         │
                                 └───────────────────┘
```

**Windows:** AutoEQ writes config for [Equalizer APO](https://sourceforge.net/projects/equalizerapo/), which hooks directly into the Windows audio pipeline.

**Linux:** AutoEQ creates a PipeWire filter-chain node acting as a virtual sink — all apps route audio through it automatically.

---

<div align="center">
  <sub>
    🔇 All audio processing is local &nbsp;·&nbsp; 🌐 No internet required &nbsp;·&nbsp; 🔓 MIT License
  </sub>
</div>

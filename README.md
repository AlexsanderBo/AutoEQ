<div align="center">
  <img src="docs/autoeq-logo.svg" width="96" height="96" alt="AutoEQ logo"/>
  <h1>AutoEQ</h1>
  <p>Real-time system-wide equalizer — warm, vocal-forward sound.<br/>No cloud. No uploads. Runs entirely on your machine.</p>

  [![Release](https://img.shields.io/github/v/release/AlexsanderBo/AutoEQ?style=flat-square&color=3b82f6)](https://github.com/AlexsanderBo/AutoEQ/releases/latest)
  [![License: MIT](https://img.shields.io/badge/license-MIT-22c55e?style=flat-square)](LICENSE)
  [![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-0ea5e9?style=flat-square)](#download)
</div>

---

## Download

<div align="center">

| Platform | Installer | Requirements |
|----------|-----------|-------------|
| **Windows 10/11** | [**⬇ AutoEQ-0.1.0-win-x64.msi**](https://github.com/AlexsanderBo/AutoEQ/releases/latest/download/AutoEQ-0.1.0-win-x64.msi) | [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) must be installed first |
| **Ubuntu / Debian** | [**⬇ autoeq-linux_0.1.0_amd64.deb**](https://github.com/AlexsanderBo/AutoEQ/releases/latest) | `pulseaudio-utils`, `pipewire`, `pipewire-pulse` |

</div>

---

## Windows setup

1. Install **[Equalizer APO](https://sourceforge.net/projects/equalizerapo/)** and reboot.
2. Run `AutoEQ-0.1.0-win-x64.msi` → Next → Install.
3. Launch **AutoEQ** from the Start Menu.
4. First launch: run as Administrator once so AutoEQ can register with Equalizer APO.

## Linux setup

```bash
sudo apt install pulseaudio-utils pipewire pipewire-pulse
sudo dpkg -i autoeq-linux_0.1.0_amd64.deb
autoeq-linux --install-pipewire --install-startup --setup-only
systemctl --user restart pipewire pipewire-pulse
```

Then select **AutoEQ Sink** in Ubuntu sound settings.

---

## Presets

| Preset | Sound |
|--------|-------|
| Reference | AutoEQ optimized |
| Cozy | Balanced warmth |
| Corner | Bass cut for desk placement |
| Tight | Tighter bass |
| Podcast | Forward mids |
| Silk | Reduced treble fatigue |
| Midnight | Low-volume smoothness |
| Flat | No EQ |

---

<div align="center">
  <sub>All audio processing is local. No internet connection required.</sub>
</div>

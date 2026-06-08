<div align="center">
  <img src="docs/banner.svg" width="900" alt="AutoEQ banner"/>

  <br/>

  [![Release](https://img.shields.io/github/v/release/AlexsanderBo/AutoEQ?style=for-the-badge&color=3b82f6&logo=github&logoColor=white)](https://github.com/AlexsanderBo/AutoEQ/releases/latest)
  [![License: MIT](https://img.shields.io/badge/License-MIT-22c55e?style=for-the-badge&logo=opensourceinitiative&logoColor=white)](LICENSE)
  [![Platform](https://img.shields.io/badge/Windows%2010%2F11-0078D4?style=for-the-badge&logo=windows&logoColor=white)](#-windows-setup)
  [![Platform](https://img.shields.io/badge/Ubuntu%20%2F%20Debian-E95420?style=for-the-badge&logo=ubuntu&logoColor=white)](#-linux-setup)
</div>

<br/>

> **AutoEQ** là equalizer hệ thống chạy hoàn toàn local — không cloud, không upload, không telemetry.  
> Âm thanh warm, vocal-forward, tối ưu cho nghe nhạc dài và podcast.

---

## 📥 Download

<div align="center">

| 🖥️ Platform | 📦 Installer | ✅ Yêu cầu |
|:---:|:---:|:---|
| **Windows 10 / 11** | [**⬇ AutoEQ-0.1.0-win-x64.msi**](https://github.com/AlexsanderBo/AutoEQ/releases/latest/download/AutoEQ-0.1.0-win-x64.msi) | [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) phải được cài trước |
| **Ubuntu / Debian** | [**⬇ autoeq-linux_0.1.0_amd64.deb**](https://github.com/AlexsanderBo/AutoEQ/releases/latest) | `pulseaudio-utils` · `pipewire` · `pipewire-pulse` |

</div>

---

## 🪟 Windows Setup

<table>
<tr>
<td width="48px" align="center">1️⃣</td>
<td>Cài <a href="https://sourceforge.net/projects/equalizerapo/"><strong>Equalizer APO</strong></a> — bắt buộc, là driver hook âm thanh hệ thống. Khởi động lại máy sau khi cài.</td>
</tr>
<tr>
<td align="center">2️⃣</td>
<td>Chạy <code>AutoEQ-0.1.0-win-x64.msi</code> → Next → Install.</td>
</tr>
<tr>
<td align="center">3️⃣</td>
<td>Mở <strong>AutoEQ</strong> từ Start Menu.</td>
</tr>
<tr>
<td align="center">4️⃣</td>
<td>Lần đầu chạy: <strong>Run as Administrator</strong> một lần để AutoEQ đăng ký với Equalizer APO. Những lần sau không cần.</td>
</tr>
</table>

**Gỡ cài đặt:**  
`Settings → Apps → Installed apps → AutoEQ → Uninstall`

---

## 🐧 Linux Setup

```bash
# 1. Cài dependencies
sudo apt install pulseaudio-utils pipewire pipewire-pulse

# 2. Cài package
sudo dpkg -i autoeq-linux_0.1.0_amd64.deb

# 3. Khởi tạo (chạy một lần)
autoeq-linux --install-pipewire --install-startup --setup-only

# 4. Restart audio services
systemctl --user restart pipewire pipewire-pulse
```

Sau đó vào **Settings → Sound → Output** và chọn **AutoEQ Sink**.

> **Gỡ cài đặt:** `sudo apt remove autoeq-linux`  
> Config cá nhân tại `~/.config/autoeq/` được giữ lại.

---

## 🎛️ Presets

Mỗi preset là một đường EQ được tinh chỉnh thủ công cho một use case cụ thể:

<div align="center">

| Preset | 🎵 Âm sắc | Dùng khi |
|:---:|:---|:---|
| **Reference** | AutoEQ signature curve | Nghe nhạc hằng ngày, mix kiểm tra |
| **Cozy** | Warm, balanced | Nhạc acoustic, jazz, lo-fi |
| **Corner** | Bass cut | Loa đặt ở góc tường bị bùng bass |
| **Tight** | Bass gọn, nhanh | EDM, hip-hop cần punch rõ |
| **Podcast** | Mid forward, vocal rõ | Podcast, audiobook, meeting |
| **Silk** | Treble giảm | Nghe lâu không mỏi tai |
| **Midnight** | Smooth ở âm lượng thấp | Nghe khuya, không muốn ồn |
| **Flat** | Không EQ | So sánh, test, căn chỉnh |

</div>

---

## ✨ Tính năng

<div align="center">

|  | Tính năng | Mô tả |
|:---:|:---|:---|
| 🔒 | **Hoàn toàn local** | Zero network — không ping, không telemetry, không cloud |
| ⚡ | **Real-time** | Latency < 10ms, xử lý toàn hệ thống |
| 🎨 | **8 presets** | Tinh chỉnh thủ công cho từng use case |
| 🖥️ | **Cross-platform** | Windows 10/11 + Ubuntu/Debian |
| 🪶 | **Nhẹ** | ~30 MB RAM, <1% CPU khi idle |
| 🔁 | **Auto-start** | Chạy cùng hệ thống, không cần mở tay |

</div>

---

## 🏗️ Cách hoạt động

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

**Windows:** AutoEQ viết config cho [Equalizer APO](https://sourceforge.net/projects/equalizerapo/) — driver hook vào audio pipeline của Windows.

**Linux:** AutoEQ tạo PipeWire filter-chain node, hoạt động như một virtual sink mà mọi app đều route qua.

---

<div align="center">
  <sub>
    🔇 All audio processing is local &nbsp;·&nbsp; 🌐 No internet required &nbsp;·&nbsp; 🔓 MIT License
  </sub>
</div>

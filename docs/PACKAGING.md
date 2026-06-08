# Packaging AutoEQ

This project ships two application surfaces:

- `AutoEQ`: Windows WPF app, packaged as portable folder or `.msi`.
- `AutoEQ.Linux`: Ubuntu/PipeWire headless runner, packaged as standalone binary or `.deb`.

Shared DSP and preset logic lives in `AutoEQ.Core`.

## Release Layout

```text
AutoEQ/                 Windows WPF app
AutoEQ.Core/            Cross-platform DSP and preset engine
AutoEQ.Linux/           Ubuntu/PipeWire runner
AutoEQ.Tests/           xUnit tests
native/                 Optional Windows native WASAPI helper
docs/                   Build, packaging, and release docs
scripts/                Build and packaging scripts
packaging/windows/      WiX MSI project
packaging/linux/        Debian package metadata
build/                  Temporary packaging workspace, ignored
dist/                   Final artifacts, ignored
```

Version metadata is centralized in `Directory.Build.props`.

## Windows Portable Build

Dependencies:

- Windows 10/11
- .NET 8 SDK
- Optional: Visual Studio Build Tools C++ workload if you need to build `native/wasapi_autoeq`
- Runtime audio backend: Equalizer APO installed by the user

Command:

```powershell
.\build.bat
```

Output:

```text
dist\AutoEQ\AutoEQ.exe
```

The wrapper calls:

```powershell
.\scripts\windows\publish-windows.ps1 -OutputDir .\dist\AutoEQ
```

## Windows MSI Build

Dependencies:

- .NET 8 SDK
- Internet/NuGet access for `WixToolset.Sdk`
- Optional: Visual Studio Build Tools C++ workload for the native helper

Command:

```powershell
.\scripts\windows\build-msi.ps1
```

Output:

```text
dist\windows\AutoEQ-0.1.0-win-x64.msi
```

MSI metadata:

- App name: `AutoEQ`
- Version: `Directory.Build.props` `Version`
- Publisher: `AutoEQ` by default, override with `-Publisher`
- Install path: `C:\Program Files\AutoEQ`
- Shortcut: Start Menu `AutoEQ`
- Main executable: `AutoEQ.exe`

Example with explicit metadata:

```powershell
.\scripts\windows\build-msi.ps1 -Version 0.1.0 -Publisher "AutoEQ"
```

Install and uninstall checks:

```powershell
msiexec /i .\dist\windows\AutoEQ-0.1.0-win-x64.msi
Start-Process "$env:ProgramFiles\AutoEQ\AutoEQ.exe"
msiexec /x .\dist\windows\AutoEQ-0.1.0-win-x64.msi
```

## Ubuntu Standalone Build

From Windows, build a self-contained linux-x64 folder:

```bat
build_linux.bat
```

Output:

```text
dist\AutoEQ.Linux\autoeq-linux
dist\AutoEQ.Linux\install_ubuntu.sh
```

Copy `dist\AutoEQ.Linux` to Ubuntu, then:

```bash
chmod +x autoeq-linux install_ubuntu.sh
./install_ubuntu.sh
systemctl --user restart pipewire pipewire-pulse
```

Ubuntu runtime dependencies:

```bash
sudo apt install pulseaudio-utils pipewire pipewire-pulse
```

Select `AutoEQ Sink` in Ubuntu sound settings after installing the PipeWire config.

## Ubuntu DEB Build

Build this on Ubuntu.

Build dependencies:

```bash
sudo apt install dotnet-sdk-8.0 dpkg-dev
```

Runtime dependencies encoded in `packaging/linux/control.template`:

```text
pulseaudio-utils, pipewire, pipewire-pulse
```

Command:

```bash
chmod +x scripts/linux/build-deb.sh
./scripts/linux/build-deb.sh
```

Output:

```text
dist/linux/autoeq-linux_0.1.0_amd64.deb
```

Install, setup, and uninstall:

```bash
sudo apt install ./dist/linux/autoeq-linux_0.1.0_amd64.deb
autoeq-linux --background
sudo apt remove autoeq-linux
```

End users can also double-click the `.deb` in Ubuntu Software, install it,
then open `AutoEQ Linux` from the app launcher. On first run the app writes
the user PipeWire config, installs user autostart, and requests `AutoEQ Sink`
as the default output.
If Ubuntu keeps the previous output selected, choose `AutoEQ Sink` once in
Sound Settings.

Installed files:

```text
/usr/bin/autoeq-linux
/usr/share/autoeq-linux/install_ubuntu.sh
/usr/share/doc/autoeq-linux/README.md
/usr/share/applications/autoeq-linux.desktop
```

User files generated at runtime are intentionally not removed by the package:

```text
~/.config/autoeq/
~/.config/autostart/autoeq-linux.desktop
~/.config/pipewire/pipewire.conf.d/99-autoeq-filter-chain.conf
```

## Notes

- WPF and Equalizer APO are Windows-only.
- Ubuntu support is currently the headless `AutoEQ.Linux` runner.
- PipeWire may need restart/reload to pick up changed EQ files. Use `--restart-on-change` only if you accept short audio interruptions.

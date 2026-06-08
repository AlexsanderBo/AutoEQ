# Release Checklist

## Preflight

- Confirm version in `Directory.Build.props`.
- Run `git status --short` and review all changed files.
- Confirm no build output is committed: `bin/`, `obj/`, `build/`, `dist/`, `.codegraph/`.
- Confirm `README.md` and `docs/PACKAGING.md` match the current commands.

## Build Verification

```powershell
dotnet restore AutoEQ.sln
dotnet build AutoEQ.sln --no-restore
dotnet test AutoEQ.Tests\AutoEQ.Tests.csproj --no-build
```

## Windows Portable

```powershell
.\build.bat
.\dist\AutoEQ\AutoEQ.exe
```

Check:

- App opens without XAML binding/compile errors.
- System tray icon appears.
- Closing the window hides it to tray.
- `Thoát hẳn` exits the process.
- Windows startup registry entry points to `AutoEQ.exe --background`.
- Equalizer APO warning appears only when APO is missing.

## Windows MSI

```powershell
.\scripts\windows\build-msi.ps1
msiexec /i .\dist\windows\AutoEQ-0.1.0-win-x64.msi
```

Check:

- App installs to `C:\Program Files\AutoEQ`.
- Start Menu shortcut exists.
- App launches from shortcut.
- App can be uninstalled:

```powershell
msiexec /x .\dist\windows\AutoEQ-0.1.0-win-x64.msi
```

- Install folder is removed after uninstall.
- User data/config remains only where expected.

## Ubuntu Standalone

```bash
chmod +x autoeq-linux install_ubuntu.sh
./install_ubuntu.sh
systemctl --user restart pipewire pipewire-pulse
./autoeq-linux --help
./autoeq-linux --background
```

Check:

- `~/.config/autoeq/AutoEQ-PipeWire.txt` is written.
- `~/.config/pipewire/pipewire.conf.d/99-autoeq-filter-chain.conf` is written.
- `AutoEQ Sink` appears in sound settings after PipeWire restart.
- `~/.config/autostart/autoeq-linux.desktop` exists when startup is installed.

## Ubuntu DEB

```bash
chmod +x scripts/linux/build-deb.sh
./scripts/linux/build-deb.sh
sudo apt install ./dist/linux/autoeq-linux_0.1.0_amd64.deb
autoeq-linux --help
autoeq-linux --background
sudo apt remove autoeq-linux
```

Check:

- `/usr/bin/autoeq-linux` exists after install.
- `/usr/share/applications/autoeq-linux.desktop` exists after install.
- First run creates `~/.config/pipewire/pipewire.conf.d/99-autoeq-filter-chain.conf`.
- `apt remove autoeq-linux` removes package-owned files.
- User config under `~/.config` is left intact.

## Artifact Naming

Expected release artifacts:

```text
dist/windows/AutoEQ-<version>-win-x64.msi
dist/linux/autoeq-linux_<version>_amd64.deb
dist/AutoEQ/                 optional Windows portable folder
dist/AutoEQ.Linux/           optional Ubuntu standalone folder
```

## Known Manual Checks

- Native Windows WASAPI helper requires MSVC C++ tools; managed build skips it when `cl.exe` is unavailable.
- Equalizer APO requires a one-time administrator run to add the include line.
- Ubuntu PipeWire EQ file reload behavior varies; `--restart-on-change` forces reload but interrupts audio briefly.

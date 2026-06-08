# End-user Install

This is the simple install path for people who only download a package and run it.

## Windows

Download:

```text
AutoEQ-<version>-win-x64.msi
```

Install:

1. Double-click the `.msi`.
2. Follow the installer.
3. Open `AutoEQ` from the Start Menu.

Installed location:

```text
C:\Program Files\AutoEQ
```

Notes:

- Equalizer APO is still required for system-wide EQ.
- Run AutoEQ as Administrator once if it needs to add its Equalizer APO include line.
- After that, normal user mode is enough.

Uninstall:

```text
Settings -> Apps -> Installed apps -> AutoEQ -> Uninstall
```

## Ubuntu

Download:

```text
autoeq-linux_<version>_amd64.deb
```

Install:

1. Double-click the `.deb`.
2. Install it with Ubuntu Software / App Center.
3. Open `AutoEQ Linux` from the app launcher.

What happens on first run:

- AutoEQ creates `~/.config/autoeq/AutoEQ-PipeWire.txt`.
- AutoEQ creates `~/.config/pipewire/pipewire.conf.d/99-autoeq-filter-chain.conf`.
- AutoEQ creates `~/.config/autostart/autoeq-linux.desktop` so it can run after login.
- AutoEQ tries to restart user PipeWire services and select `AutoEQ Sink`.

If audio still uses the old output:

```text
Settings -> Sound -> Output -> AutoEQ Sink
```

Uninstall:

```bash
sudo apt remove autoeq-linux
```

User config is left in `~/.config` so uninstalling does not delete personal settings.

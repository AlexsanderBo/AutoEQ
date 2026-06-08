# Per-device EQ spec

## Goal
- Keep independent Equalizer APO EQ blocks per Windows output endpoint.
- Switching default output must not overwrite other device EQ.
- Use `Device:` scoped blocks with endpoint ID from `AudioOutputInfo.DefaultDeviceId`.

## Constitution Check
- Local-only: no cloud, telemetry, network, or audio upload.
- APO safe write: keep atomic write for `AutoEQ_autoeq.txt`; only add/keep single `Include: AutoEQ_autoeq.txt` in `config.txt`; keep `config.txt.bak` backup.
- Stable EQ cadence unchanged: do not alter 5s cadence, decision window, 3/4 consensus, or cooldown.
- UI responsive: all file I/O remains async/background from orchestration path.
- No new package.

## Behavior
- Generated APO file has one shared header, then one block per device.
- Each device block starts with `Device: <DefaultDeviceId>` when endpoint ID exists.
- Device name appears only as comments/profile text; never as default match key.
- Applying preset for device A updates only device A record and rewrites full generated file from store.
- Applying preset for device B preserves device A block.
- Limiter guard runs on one device block only before store upsert.
- Empty endpoint ID logs fallback warning and uses normalized name token only when available; otherwise writes global legacy block.
- Old `WriteAutoEQConfigAsync` remains global compatibility wrapper.

## Manual APO acceptance
1. Enable Equalizer APO on at least two render endpoints.
2. Run AutoEQ.
3. Switch default output between endpoints.
4. Open `C:\Program Files\EqualizerAPO\config\AutoEQ_autoeq.txt`.
5. Confirm multiple `Device: {0.0.0.00000000}.{...}` blocks exist.
6. Confirm each device keeps own preset/EQ when switching back.
7. Confirm no manual `config.txt` lines changed except one include.

## Validate
```bat
dotnet build AutoEQ.sln
dotnet test AutoEQ.sln --no-build
```
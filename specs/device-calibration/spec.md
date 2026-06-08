# Device calibration spec

## Goal
- Learn per-device target voicing from real playback measurements.
- Keep existing inferred targets until calibration is ready.
- Write only `OutputAudioProfile.TargetBass/TargetWarmth/TargetVocal/TargetBright` for device key.
- Leave Auto EQ cadence, cooldown, consensus, limiter, and APO write path unchanged.

## Constitution Check
- Local-only: no cloud, telemetry, network, upload, or external measurement service.
- Stable EQ cadence unchanged: do not alter 5s cadence, decision window, 3/4 consensus, or cooldown.
- APO safety: calibration never writes APO directly; preset apply still owns APO file writes.
- Audio safety: passive learning by default; no unexpected loud tone.
- UI responsive: measurement accumulation runs off UI thread; UI updates use dispatcher `Post` pattern.
- No new package.

## Behavior
- Each Windows output device is calibrated independently by `AudioOutputInfo.DefaultDeviceId`.
- Old profile JSON without calibration fields still loads with safe defaults.
- Bad frames are ignored: silence, clipping, broadband noise, invalid bands.
- Feedback loop is avoided by compensating current applied EQ before averaging measured bands.
- Calibration becomes ready only after enough stable valid frames.
- Ready calibration sets `IsCalibrated=true`, `CalibratedUtc`, `CalibrationSampleCount`, and `MeasuredBandAverage`.
- Reset clears calibration and restores inferred device targets.

## Validate
```bat
dotnet build AutoEQ.sln
dotnet test AutoEQ.sln --no-build
```

## Manual acceptance
1. Start AutoEQ with Auto EQ enabled.
2. Play varied music on one output device.
3. Confirm calibration badge progresses from learning to calibrated.
4. Switch output device and confirm separate progress/state.
5. Reset calibration and confirm target returns to inferred behavior.
6. Confirm APO file is changed only by normal preset apply path.
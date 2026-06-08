# Device calibration plan

1. Add Spec Kit documents with Constitution Check.
2. Extend `OutputAudioProfile` with calibration metadata.
3. Extend `OutputAudioProfileStore` with `ApplyCalibration` and `ResetCalibration`.
4. Add `DynamicVoicing.DeriveTargetsFromMeasurement(...)`.
5. Add `CalibrationService` passive learner with bad-frame rejection and feedback compensation.
6. Wire `AppOrchestrator` to feed features, apply ready calibration, and refresh VM state.
7. Add `MainViewModel` state/commands and XAML badge/progress UI.
8. Add unit tests for derivation, store persistence/reset, bad-frame rejection, convergence, and compensation.
9. Run build and tests.

## Structure
- `AutoEQ/Models/OutputAudioProfile.cs` adds persisted fields.
- `AutoEQ/Services/OutputAudioProfileStore.cs` persists/reset calibration.
- `AutoEQ/Services/DynamicVoicing.cs` derives four targets from measured 7-band average.
- `AutoEQ/Services/CalibrationService.cs` accumulates passive measurements per device.
- `AutoEQ/Services/PresetEngine.cs` exposes last applied dynamic gains for feedback compensation.
- `AutoEQ/Services/AppOrchestrator.cs` orchestrates calibration.
- `AutoEQ/ViewModels/MainViewModel.cs` exposes UI state and commands.
- `AutoEQ/MainWindow.xaml` shows compact calibration badge.
- `AutoEQ.Tests/*` covers calibration behavior.

## Feedback compensation
- Calibration observes audio after EQ may already be active.
- Current applied EQ is projected from dynamic bands to 7 feature bands.
- Projected positive gain lowers learned measured energy; negative gain raises it.
- Compensation is clamped so bad state cannot overcorrect calibration.

## Target clamp
- Calibration starts from inferred profile targets.
- Measurement only trims targets within small per-macro windows.
- Bass-heavy device lowers bass target; dark device raises bright target.
- Clamp prevents target from requiring boost beyond `MaxBoostDb` or breaking `BassSafetyCutDb` intent.
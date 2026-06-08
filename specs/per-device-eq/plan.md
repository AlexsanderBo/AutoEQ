# Per-device EQ plan

1. Add `DeviceEqStore` with JSON persistence beside output profiles.
2. Add `DynamicVoicing.BuildDeviceScopedBlock(...)` while keeping existing text builder.
3. Refactor `EqualizerApoManager` to write multi-block config from store.
4. Keep legacy `WriteAutoEQConfigAsync` as global wrapper.
5. Add device-aware apply path through `PresetApplyService` and `AppOrchestrator`.
6. Track current `AudioOutputInfo` on startup and device changes.
7. Add tests for store, device blocks, multi-device writer, limiter per block, fallback.
8. Build and test solution.

## Structure
- `AutoEQ/Services/DeviceEqStore.cs` new persistent per-device EQ state.
- `AutoEQ/Services/DynamicVoicing.cs` new device-scoped block helper.
- `AutoEQ/Services/EqualizerApoManager.cs` multi-device config writer.
- `AutoEQ/Services/PresetApplyService.cs` device-aware apply overload.
- `AutoEQ/Services/AppOrchestrator.cs` route current endpoint identity.
- `AutoEQ.Tests/*` updated coverage.

## Device pattern rule
- Primary match pattern: `AudioOutputInfo.DefaultDeviceId`.
- Device key: same endpoint ID.
- Name only comment/profile text.
- Fallback: normalized name token, warning log; if no reliable token, global block.
# AutoEQ Constitution

## Core Principles

### I. Local-Only, Open-Source Audio Processing
AutoEQ MUST analyze audio only on the user's Windows PC. Features MUST NOT upload audio, call cloud AI, use external APIs, or add telemetry. Runtime dependencies MUST be open source and must run locally.

### II. Safe Equalizer APO Integration
The application MUST preserve the user's Equalizer APO setup. It may add the managed include line and write only its generated Auto EQ file. Changes to Equalizer APO configuration MUST create or preserve a backup, use atomic or low-risk file writes where practical, and surface administrator-permission failures clearly.

### III. Stable, Non-Disruptive EQ Decisions
Automatic EQ changes MUST avoid rapid switching and inaudible churn. The app MUST analyze audio every 5 seconds, evaluate decisions over a 20-second window, and change presets only when 3 of the last 4 analyses agree. Preset changes MUST have a minimum 30-second cooldown. The app MUST NOT change EQ continuously per audio chunk.

### IV. Responsive, Separated Desktop Architecture
Implementation MUST fit the existing C# .NET WPF architecture unless a spec justifies a structural change. The UI MUST remain responsive while audio work runs in background services. Audio capture, analysis, decision engine, preset management, Equalizer APO writing, logging, now-playing, and UI orchestration MUST remain separated by service/model responsibilities.

### V. Testable, Recoverable, User-Controlled Changes
All behavior MUST be testable through focused automated tests, build validation, or documented manual checks. Changes affecting DSP, preset selection, file writes, manual override, or UI state MUST be small, reviewable, and recoverable. The app MUST preserve user control, including the ability to disable Auto EQ or manually choose a preset.

## Technology and Dependency Constraints

The primary application targets Windows desktop with C# .NET 8 or .NET 9, WPF, NAudio for Windows audio capture, optional local DSP/FFT utilities, and Equalizer APO as the open-source EQ backend. New runtime dependencies MUST be open source, local-only, and justified in the implementation plan. Closed-source dependencies, telemetry SDKs, and cloud-only services are prohibited. Native code may be used only when the plan documents its build path, local execution model, and relationship to the WPF app.

## Development Workflow and Quality Gates

Feature work MUST follow the Spec Kit sequence: constitution, specification, clarification if needed, implementation plan, tasks, then implementation. Each feature MUST have `spec.md`, `plan.md`, and `tasks.md` under `specs/<feature-name>/` before coding starts. Plans MUST include a constitution check, real project structure, validation commands, and any manual checks required for audio or Equalizer APO behavior.

## Governance

This constitution supersedes ad-hoc implementation choices. Amendments require updating this file, documenting why the change is needed, and reviewing active specs/plans for impact. If a feature conflicts with these principles, its plan MUST document the violation, why it is necessary, and the simpler alternative that was rejected.

**Version**: 1.0.0 | **Ratified**: 2026-05-30 | **Last Amended**: 2026-05-30

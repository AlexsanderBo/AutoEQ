# AutoEQ UI redesign spec

## Mục tiêu
- Thay Window rỗng bằng UI WPF thật bám `autoeq_ui_mockup_v2.html`.
- Nối service có sẵn vào `MainViewModel` qua composition root duy nhất.
- Giữ local-only, không WebView, không package mới.

## Constitution Check
- Local-only: chỉ dùng service local, không telemetry/cloud/upload.
- APO an toàn: mọi ghi Equalizer APO đi qua `PresetApplyService`/`IEqualizerApoManager`.
- Cadence ổn định: phân tích theo `AppConfig.AnalysisIntervalSeconds`, window/cooldown do `PresetEngine` giữ.
- UI mượt: event nền cập nhật VM qua `Post` hoặc hàm VM đã marshal.
- Không sửa logic DSP/preset/config/native/tests.

## UI scope
- 3 cột: left control, center now-playing/log, right spectrum/curve/metrics.
- Bind vào surface VM có sẵn và phần bổ sung: room/night/loudness toggles, media commands, log, EQ curve.
- Font ưu tiên Oswald/Archivo/Space Mono nếu local có trong `/Fonts`; fallback Segoe UI.

## Validate
- `dotnet build AutoEQ.sln`
- `dotnet test AutoEQ.sln --no-build`
- `run_dev.bat`
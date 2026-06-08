# AutoEQ UI redesign plan

1. Dọn UI cũ trong phạm vi hẹp: `App.xaml`, `MainWindow.xaml`, remove scratch excludes trong csproj.
2. Tạo resource dictionaries cho màu/control/converters.
3. Bổ sung `MainViewModel`: toggles, media commands, now-playing artist/play state, signal log, preset list, EQ path.
4. Tạo `AppOrchestrator`: khởi tạo service graph, set DataContext, đăng ký event 2 chiều, quản lý vòng đời.
5. Cập nhật `App.xaml.cs`: giữ auto-start, tạo Window/orchestrator, dispose khi thoát.
6. Build/test, sửa lỗi XAML/C#.

## Ghi chú kỹ thuật
- Orchestrator không chạm UI element.
- Service event nền không set property VM trực tiếp.
- EQ curve parse từ `EqPreset.EqualizerApoText`, vẽ Path tần suất thấp.
- Now-playing timer 1.5s.
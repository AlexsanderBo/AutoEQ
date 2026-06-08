using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using AutoEQ.ViewModels;

namespace AutoEQ.Views;

public partial class MainWindow : Window
{
    private const double VinylTargetSpinSpeed = 200.0;
    // Góc kim: hạ vào đĩa khi phát, nhấc ra ngoài khi dừng. Transition trong XAML lo phần mượt.
    private const double TonearmPlayAngle = 0.0;
    private const double TonearmRestAngle = 28.0;
    private readonly DispatcherTimer _vinylTimer;
    private RotateTransform? _vinylRotate;
    private RotateTransform? _tonearmRotate;
    private double _vinylAngle;
    private DateTime _lastVinylFrameUtc;
    private MainViewModel? _vm;

    public MainWindow()
    {
        InitializeComponent();
        // 30fps is smooth enough for a slow record spin and halves UI-thread load vs 60fps.
        _vinylTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _vinylTimer.Tick += OnVinylTick;
        Opened += OnOpened;
        Closed += OnClosed;
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as MainViewModel;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
        UpdateTonearm();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Đĩa quay liên tục; chỉ kim phản ứng theo trạng thái phát/dừng.
        if (e.PropertyName == nameof(MainViewModel.IsPlaying))
            UpdateTonearm();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _vinylRotate = this.FindControl<Grid>("VinylDisc")?.RenderTransform as RotateTransform;
        _tonearmRotate = this.FindControl<Canvas>("Tonearm")?.RenderTransform as RotateTransform;
        _lastVinylFrameUtc = DateTime.UtcNow;
        // Mâm đĩa luôn quay khi cửa sổ mở, không phụ thuộc play/pause.
        if (!_vinylTimer.IsEnabled) _vinylTimer.Start();
        UpdateTonearm();
    }

    // Kim hạ vào rãnh đĩa khi đang phát, nhấc ra khi dừng.
    private void UpdateTonearm()
    {
        if (_tonearmRotate is null) return;
        _tonearmRotate.Angle = (_vm?.IsPlaying ?? false) ? TonearmPlayAngle : TonearmRestAngle;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _vinylTimer.Stop();
        _vinylTimer.Tick -= OnVinylTick;
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnVinylTick(object? sender, EventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        double deltaSeconds = (now - _lastVinylFrameUtc).TotalSeconds;
        _lastVinylFrameUtc = now;
        if (deltaSeconds <= 0 || deltaSeconds > 0.1) deltaSeconds = 0.033;

        // Quay đều ở tốc độ cố định bất kể play/pause.
        _vinylAngle = (_vinylAngle + (VinylTargetSpinSpeed * deltaSeconds)) % 360;
        if (_vinylRotate is not null) _vinylRotate.Angle = _vinylAngle;
    }
}

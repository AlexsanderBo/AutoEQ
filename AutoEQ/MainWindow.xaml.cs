using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using Forms = System.Windows.Forms;
using Orientation = System.Windows.Controls.Orientation;
using Point = System.Windows.Point;
using NAudio.CoreAudioApi;
using AutoEQ.Config;
using AutoEQ.Models;
using AutoEQ.Services;

namespace AutoEQ;

public partial class MainWindow : Window
{
    private readonly AppLogger _logger = new();
    private readonly AudioCaptureService _captureService = new(new DspAnalyzer());
    private readonly PresetEngine _presetEngine = new();
    private readonly EqualizerApoManager _apoManager = new();
    private readonly PresetApplyService _presetApplyService;
    private readonly NativeWasapiAutoEqClient _nativeAutoEqClient = new();
    private readonly NowPlayingService _nowPlayingService = new();
    private readonly SystemVolumeService _systemVolumeService = new();
    private readonly OutputAudioProfileStore _outputProfileStore = new();
    private readonly DispatcherTimer _nowPlayingTimer = new();
    private readonly DispatcherTimer _volumeSyncTimer = new();
    private readonly DispatcherTimer _audioOutputSyncTimer = new();
    private readonly Forms.NotifyIcon _trayIcon = new();
    private readonly Random _waveRandom = new();
    private double _smoothInputPeak;
    private double _smoothInputRms;
    private double _smoothInputCrest = 0.12;
    private AudioOutputInfo? _audioOutputInfo;
    private OutputAudioProfile _currentOutputProfile = new();
    private string _lastAudioOutputKey = string.Empty;
    private string _currentPresetName = "Universal Warm Balance";
    private bool _initialized;
    private bool _updatingPresetFromAuto;
    private bool _syncingSystemVolume;
    private bool _draggingVolumeKnob;
    private int _systemVolumePercent;
    private int? _pendingVolumePercent;
    private DateTime _lastVolumeCommitUtc = DateTime.MinValue;
    private const double VolumeKnobCenter = 83;
    private const double VolumeKnobMinAngle = -135;
    private const double VolumeKnobMaxAngle = 135;
    private string _lastNowPlayingKey = string.Empty;
    private bool _refreshingNowPlaying;
    private bool _recordIsSpinning;
    private bool _isMonitoring;
    private bool _detectingAudioOutput;
    private DateTime _lastFeatureUiUpdateUtc = DateTime.MinValue;
    private DateTime _lastFeatureLogUtc = DateTime.MinValue;
    private AudioFeatures? _latestFeatures;
    private bool _featureUiQueued;
    private bool _autoEqVisualEnabledState;
    private DateTime _lastAutoEqSwitchUtc = DateTime.MinValue;
    private readonly CancellationTokenSource _windowCts = new();
    private bool _usingNativeAutoEq;
    private DateTime _lastNativeSnapshotUtc = DateTime.MinValue;
    private DateTime _lastWaveformUiUtc = DateTime.MinValue;
    private readonly double[] _smoothDashboardLevels = { 0.34, 0.48, 0.52, 0.58, 0.50, 0.44, 0.38 };
    private readonly double[] _smoothWaveLevels = { 0.24, 0.36, 0.48, 0.62, 0.48, 0.36, 0.24 };
    private double _smoothDashboardDrive;
    private double _smoothBassMeter = 0.42;
    private double _smoothVocalMeter = 0.52;
    private double _smoothTrebleMeter = 0.46;
    private double _smoothSignalFlowWidth = 24;
    private bool _isTrayHidden;
    private bool _allowClose;
    private DateTime _lastHiddenVolumeSyncUtc = DateTime.MinValue;
    private bool IsUiRenderingEnabled => !_isTrayHidden && IsVisible && WindowState != WindowState.Minimized;

    public MainWindow()
    {
        _presetApplyService = new PresetApplyService(_apoManager);
        InitializeComponent();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        StateChanged += MainWindow_StateChanged;
        Closing += MainWindow_Closing;
        InitializeTrayIcon();
        SyncShortcutTextToVisibleUi();
        PresetComboBox.ItemsSource = _presetEngine.GetPresetNames();
        PresetComboBox.SelectedItem = _currentPresetName;
        _ = DetectSystemAudioOutputAsync(applyBestPreset: true);
        SyncSystemVolumeFromWindows();
        _initialized = true;

        _logger.MessageLogged += (_, entry) => Dispatcher.InvokeAsync(() => AddLog(entry), DispatcherPriority.Background);
        ((IDspAnalyzer)_captureService.Analyzer).ErrorOccurred += (_, message) => _logger.Error(message);
        _captureService.FeaturesAvailable += CaptureService_FeaturesAvailable;
        _captureService.WaveformAvailable += CaptureService_WaveformAvailable;
        _captureService.ErrorOccurred += (_, message) => _logger.Error(message);
        _nativeAutoEqClient.SnapshotAvailable += NativeAutoEqClient_SnapshotAvailable;
        _nativeAutoEqClient.ErrorOccurred += (_, message) => _logger.Error($"Native WASAPI AutoEQ: {message}");
        _captureService.DeviceChanged += (_, device) => Dispatcher.Invoke(() =>
        {
            _ = DetectSystemAudioOutputAsync(applyBestPreset: false);
        });

        _nowPlayingTimer.Interval = TimeSpan.FromSeconds(2);
        _nowPlayingTimer.Tick += async (_, _) => await RefreshNowPlayingAsync();
        _nowPlayingTimer.Start();
        _ = RefreshNowPlayingAsync();

        _volumeSyncTimer.Interval = TimeSpan.FromMilliseconds(120);
        _volumeSyncTimer.Tick += (_, _) => FlushVolumeOrSyncFromWindows();
        _volumeSyncTimer.Start();

        _audioOutputSyncTimer.Interval = TimeSpan.FromSeconds(3);
        _audioOutputSyncTimer.Tick += (_, _) => _ = DetectSystemAudioOutputAsync(applyBestPreset: AutoEqCheckBox.IsChecked == true);
        _audioOutputSyncTimer.Start();

        _logger.Info("Sẵn sàng. Âm thanh chỉ xử lý trên máy, không gửi lên cloud/API ngoài.");
        UpdateWaveBars(null);
        UpdateInputScanner(Array.Empty<float>());
        UpdateAudioMood(null);
        UpdateDashboardCharts(null, 0);
        UpdateSoundOutputList();
        UpdateAutoEqVisualState();
        SetAutoEqEnabled(AutoEqCheckBox.IsChecked == true);
        if (!_apoManager.IsInstalled())
        {
            _logger.Error("Equalizer APO config folder was not found. Install Equalizer APO or check the path.");
        }
    }

    private void InitializeTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Mở AutoEQ", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add("Bật/Tắt Auto EQ", null, (_, _) => Dispatcher.Invoke(() => AutoEqCheckBox.IsChecked = AutoEqCheckBox.IsChecked != true));
        menu.Items.Add("Thoát", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        _trayIcon.Text = "AutoEQ - chạy ngầm tối ưu";
        _trayIcon.Icon = System.Drawing.SystemIcons.Application;
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.Visible = true;
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose) return;

        e.Cancel = true;
        HideToTray();
    }

    private void HideToTray()
    {
        _isTrayHidden = true;
        ShowInTaskbar = false;
        Hide();
        _trayIcon.Visible = true;
        _trayIcon.Text = AutoEqCheckBox.IsChecked == true ? "AutoEQ đang chạy ngầm" : "AutoEQ đang tắt";
    }

    private void ShowFromTray()
    {
        _isTrayHidden = false;
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        RefreshVisibleUiFromState();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private void RefreshVisibleUiFromState()
    {
        UpdateAutoEqVisualState(force: true);
        UpdateWaveBars(_latestFeatures);
        UpdateInputScanner(Array.Empty<float>());
        UpdateAudioMood(_latestFeatures);
        UpdateDashboardCharts(_latestFeatures, Math.Clamp((_latestFeatures?.Rms ?? 0) * 12, 0, 1));
        SyncSystemVolumeFromWindows();
    }

    private void SyncShortcutTextToVisibleUi()
    {
        const string shortcutText = "Phím tắt: Ctrl+E Auto EQ | Ctrl+R đồng bộ sound output | Ctrl+Enter áp preset";

        ShortcutLabel.Text = shortcutText;
        AutoEqStatusHintLabel.Text = shortcutText;
        CurrentPresetLabel.ToolTip = shortcutText;
        AutoEqCheckBox.ToolTip = shortcutText;
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        await ApplyPresetAsync(_presetEngine.GetPreset(_currentPresetName), "Manual apply");
    }

    private void OpenConfigButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _apoManager.OpenConfigFolder();
        }
        catch (Exception ex)
        {
            _logger.Error(ex.Message);
        }
    }

    private void StartMonitoring()
    {
        if (_isMonitoring) return;

        _captureService.Start();
        _isMonitoring = true;
        MonitoringStatusLabel.Text = "ĐANG NGHE";
        _logger.Info("Auto EQ đã bật. Đang nghe và phân tích âm thanh.");
    }

    private void StopMonitoring()
    {
        if (!_isMonitoring) return;

        _captureService.Stop();
        _isMonitoring = false;
        DeviceLabel.Text = FormatMainboardLabel();
        SoundCardLabel.Text = FormatSoundCardLabel();
        MonitoringStatusLabel.Text = "SẴN SÀNG";
        _logger.Info("Auto EQ đã tắt. Đã dừng phân tích âm thanh.");
    }

    private async void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initialized || PresetComboBox.SelectedItem is not string presetName) return;
        _currentPresetName = presetName;
        UpdateCurrentPresetLabel();
        UpdateAudioMood(null);

        if (!_updatingPresetFromAuto && AutoEqCheckBox.IsChecked == true)
        {
            AutoEqCheckBox.IsChecked = false;
            _logger.Info("Đã chọn preset thủ công. Auto EQ đã tắt.");
        }

        if (AutoEqCheckBox.IsChecked == false)
        {
            await ApplyPresetAsync(_presetEngine.GetPreset(_currentPresetName), "Manual preset selected");
        }
    }

    private void VolumeKnob_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggingVolumeKnob = true;
        ((UIElement)sender).CaptureMouse();
        SetVolumeFromKnobPoint(e.GetPosition((IInputElement)sender));
    }

    private void VolumeKnob_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_draggingVolumeKnob) return;
        SetVolumeFromKnobPoint(e.GetPosition((IInputElement)sender));
    }

    private void VolumeKnob_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggingVolumeKnob = false;
        CommitPendingVolumeIfDue(force: true);
        ((UIElement)sender).ReleaseMouseCapture();
    }

    private void VolumeKnob_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        SetSystemVolume(_systemVolumePercent + (e.Delta > 0 ? 2 : -2));
    }

    private void AutoEqCheckBox_StateChanged(object sender, RoutedEventArgs e)
    {
        if (AutoEqCheckBox is null) return;

        AutoEqCheckBox.Content = AutoEqCheckBox.IsChecked == true ? "BẬT" : "TẮT";
        if (!_initialized) return;

        SetAutoEqEnabled(AutoEqCheckBox.IsChecked == true);
        UpdateAutoEqVisualState();
        UpdateAudioMood(null);
    }

    private async void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.E)
        {
            AutoEqCheckBox.IsChecked = AutoEqCheckBox.IsChecked != true;
            _logger.Info($"Shortcut Ctrl+E: Auto EQ {(AutoEqCheckBox.IsChecked == true ? "bật" : "tắt")}.");
            e.Handled = true;
            return;
        }

        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.R)
        {
            _ = DetectSystemAudioOutputAsync(applyBestPreset: true);
            _logger.Info("Shortcut Ctrl+R: đã đồng bộ lại sound output và chọn AutoEQ tốt nhất.");
            e.Handled = true;
            return;
        }

        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.Enter)
        {
            await ApplyPresetAsync(_presetEngine.GetPreset(_currentPresetName), "Shortcut apply");
            _logger.Info($"Shortcut Ctrl+Enter: đã áp preset {_currentPresetName}.");
            e.Handled = true;
            return;
        }

        if (e.KeyboardDevice.Modifiers == ModifierKeys.None && e.Key == Key.Space)
        {
            await RunMediaCommandAsync(() => _nowPlayingService.PlayPauseAsync(), "Play/Pause shortcut");
            _logger.Info("Shortcut Space: phát/tạm dừng.");
            e.Handled = true;
            return;
        }

        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.Left)
        {
            await RunMediaCommandAsync(() => _nowPlayingService.PreviousAsync(), "Previous shortcut");
            _logger.Info("Shortcut Ctrl+Left: bài trước.");
            e.Handled = true;
            return;
        }

        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.Right)
        {
            await RunMediaCommandAsync(() => _nowPlayingService.NextAsync(), "Next shortcut");
            _logger.Info("Shortcut Ctrl+Right: bài tiếp.");
            e.Handled = true;
            return;
        }

        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.Up)
        {
            SetSystemVolume(_systemVolumePercent + 5);
            CommitPendingVolumeIfDue(force: true);
            _logger.Info($"Shortcut Ctrl+Up: âm lượng {_systemVolumePercent}%.");
            e.Handled = true;
            return;
        }

        if (e.KeyboardDevice.Modifiers == ModifierKeys.Control && e.Key == Key.Down)
        {
            SetSystemVolume(_systemVolumePercent - 5);
            CommitPendingVolumeIfDue(force: true);
            _logger.Info($"Shortcut Ctrl+Down: âm lượng {_systemVolumePercent}%.");
            e.Handled = true;
        }
    }

    private void SetAutoEqEnabled(bool enabled)
    {
        if (enabled)
        {
            _ = DetectSystemAudioOutputAsync(applyBestPreset: true);
            StartMonitoring();
        }
        else
        {
            StopMonitoring();
        }
    }

    private void SetVolumeFromKnobPoint(System.Windows.Point point)
    {
        double dx = point.X - VolumeKnobCenter;
        double dy = point.Y - VolumeKnobCenter;
        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        double knobAngle = angle < VolumeKnobMinAngle ? angle + 360 : angle;

        // Hardware-style 270-degree travel: stop hard at 0%/100%, no wrap-around jump.
        if (knobAngle > VolumeKnobMaxAngle)
        {
            int edgeVolume = _systemVolumePercent <= 50 ? 0 : 100;
            SetSystemVolume(edgeVolume);
            return;
        }

        knobAngle = Math.Clamp(knobAngle, VolumeKnobMinAngle, VolumeKnobMaxAngle);
        int volume = (int)Math.Round((knobAngle - VolumeKnobMinAngle) / (VolumeKnobMaxAngle - VolumeKnobMinAngle) * 100);
        SetSystemVolume(volume);
    }

    private void SetSystemVolume(int volume)
    {
        volume = Math.Clamp(volume, 0, 100);
        if (volume == _systemVolumePercent && _pendingVolumePercent == null) return;

        _systemVolumePercent = volume;
        UpdateVolumeKnob(volume);

        if (!_initialized || _syncingSystemVolume) return;
        _pendingVolumePercent = volume;
    }

    private void FlushVolumeOrSyncFromWindows()
    {
        if (!IsUiRenderingEnabled && !_pendingVolumePercent.HasValue)
        {
            if (DateTime.UtcNow - _lastHiddenVolumeSyncUtc < TimeSpan.FromSeconds(1)) return;
            _lastHiddenVolumeSyncUtc = DateTime.UtcNow;
        }

        if (_pendingVolumePercent.HasValue)
        {
            CommitPendingVolumeIfDue();
            return;
        }

        if (!_draggingVolumeKnob)
        {
            SyncSystemVolumeFromWindows();
        }
    }

    private void CommitPendingVolumeIfDue(bool force = false)
    {
        if (!_pendingVolumePercent.HasValue) return;
        if (!force && DateTime.UtcNow - _lastVolumeCommitUtc < TimeSpan.FromMilliseconds(80)) return;

        int volume = _pendingVolumePercent.Value;
        _pendingVolumePercent = null;
        _lastVolumeCommitUtc = DateTime.UtcNow;

        try
        {
            _systemVolumeService.SetMasterVolumePercent(volume);
        }
        catch (Exception ex)
        {
            _logger.Error($"Could not set Windows volume: {ex.Message}");
        }
    }

    private void CaptureService_FeaturesAvailable(object? sender, AudioFeatures features)
    {
        _latestFeatures = features;
        if (_featureUiQueued) return;

        _featureUiQueued = true;
        Dispatcher.InvokeAsync(ProcessLatestFeaturesAsync, DispatcherPriority.Background);
    }

    private void CaptureService_WaveformAvailable(object? sender, float[] waveform)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!IsUiRenderingEnabled) return;
            if (DateTime.UtcNow - _lastWaveformUiUtc < TimeSpan.FromMilliseconds(70)) return;
            _lastWaveformUiUtc = DateTime.UtcNow;
            UpdateInputScanner(waveform);
        }, DispatcherPriority.Render);
    }

    private void NativeAutoEqClient_SnapshotAvailable(object? sender, NativeAutoEqSnapshot snapshot)
    {
        _usingNativeAutoEq = true;
        _lastNativeSnapshotUtc = DateTime.UtcNow;
        Dispatcher.InvokeAsync(() => ProcessNativeSnapshotAsync(snapshot), DispatcherPriority.Background);
    }

    private async Task ProcessNativeSnapshotAsync(NativeAutoEqSnapshot snapshot)
    {
        if (_windowCts.IsCancellationRequested) return;

        AudioFeatures features = PresetEngine.ToAudioFeatures(snapshot);
        _latestFeatures = features;
        if (IsUiRenderingEnabled)
        {
            AnalysisStateLabel.Text = $"Trạng thái: Native {TranslateAnalysisState(features.State)} | RMS {features.Rms:F4}";
            GenreLabel.Text = $"Nguồn phân tích: WASAPI native ({snapshot.Device})";
            UpdateWaveBars(features);
            UpdateAudioMood(features);
            UpdateDashboardCharts(features, Math.Clamp(features.Rms * 12, 0, 1));
        }

        if (AutoEqCheckBox.IsChecked != true) return;
        if (_apoManager.IsLikelyInstalledOnCurrentDevice(_audioOutputInfo))
        {
            _logger.Info("Đã phát hiện APO trên thiết bị hiện tại. Ưu tiên WASAPI native để phân tích sạch hơn.");
        }

        EqPreset preset = _presetEngine.BuildNativeAutoEqPreset(snapshot, _currentOutputProfile, NearWallCheckBox.IsChecked == true, NightModeCheckBox.IsChecked == true);
        _currentPresetName = preset.Name;
        _updatingPresetFromAuto = true;
        PresetComboBox.SelectedItem = PresetComboBox.Items.Contains(preset.Name) ? preset.Name : null;
        _updatingPresetFromAuto = false;
        if (IsUiRenderingEnabled) UpdateCurrentPresetLabel();
        await ApplyPresetAsync(preset, "Native WASAPI Auto EQ");
        _presetEngine.RememberAppliedCurve(preset);
    }

    private async Task ProcessLatestFeaturesAsync()
    {
        if (_windowCts.IsCancellationRequested) return;

        AudioFeatures? features = _latestFeatures;
        _latestFeatures = null;
        _featureUiQueued = false;
        if (features is null) return;

        bool nativeFresh = _usingNativeAutoEq && DateTime.UtcNow - _lastNativeSnapshotUtc < TimeSpan.FromSeconds(AppConfig.NativeFreshnessSeconds);
        if (_usingNativeAutoEq && !nativeFresh)
        {
            _usingNativeAutoEq = false;
            _logger.Info("Snapshot native đã cũ, cho phép engine DSP ghi EQ trở lại để tránh kẹt curve.");
        }

        bool refreshVisuals = DateTime.UtcNow - _lastFeatureUiUpdateUtc >= TimeSpan.FromMilliseconds(850);
        if (refreshVisuals && IsUiRenderingEnabled)
        {
            _lastFeatureUiUpdateUtc = DateTime.UtcNow;
            AnalysisStateLabel.Text = $"Trạng thái: {TranslateAnalysisState(features.State)} | RMS {features.Rms:F4}";
            GenreLabel.Text = $"Thể loại: {TranslateGenre(features.GenreHint)}";
            UpdateWaveBars(features);
            UpdateAudioMood(features);
            UpdateSignalEqInteraction(features, 0);
            UpdateDashboardCharts(features, Math.Clamp(features.Rms * 12, 0, 1));
        }

        bool writeAnalysisLog = DateTime.UtcNow - _lastFeatureLogUtc >= TimeSpan.FromSeconds(5);
        if (writeAnalysisLog)
        {
            _lastFeatureLogUtc = DateTime.UtcNow;
            _logger.Info(FormatReadableAnalysisLog(features));
        }

        if (nativeFresh)
        {
            if (writeAnalysisLog)
            {
                _logger.Info("Engine native đang giữ EQ; engine DSP chỉ cập nhật UI vì snapshot native còn tươi.");
            }
            return;
        }

        PresetEngine.PresetDecision decision = _presetEngine.EvaluateDynamicAutoEq(
            features,
            _currentOutputProfile,
            AutoEqCheckBox.IsChecked == true,
            NearWallCheckBox.IsChecked == true,
            NightModeCheckBox.IsChecked == true);

        if (writeAnalysisLog && !string.IsNullOrWhiteSpace(decision.Reason))
        {
            _logger.Info(decision.Reason);
        }

        if (decision.ShouldSwitch && DateTime.UtcNow - _lastAutoEqSwitchUtc >= TimeSpan.FromMilliseconds(900))
        {
            _lastAutoEqSwitchUtc = DateTime.UtcNow;
            EqPreset requested = decision.RequestedPreset ?? _presetEngine.GetPreset(decision.TargetPresetName);
            _currentPresetName = requested.Name;
            _updatingPresetFromAuto = true;
            PresetComboBox.SelectedItem = requested.Name;
            _updatingPresetFromAuto = false;
            if (IsUiRenderingEnabled)
            {
                UpdateCurrentPresetLabel();
                UpdateAudioMood(features);
            }
            await ApplyPresetAsync(requested, "Auto EQ switched preset");
            if (requested.IsDynamic)
            {
                _presetEngine.RememberAppliedCurve(requested);
            }
        }
    }

    private void UpdateAudioMood(AudioFeatures? features)
    {
        double energy = features is null ? 0.62 : Math.Clamp(features.Rms * 14, 0, 1);
        double bassWeight = features is null ? 0.42 : Math.Clamp(features.SubBass + features.Bass, 0, 1);
        double warmthWeight = features is null ? 0.36 : Math.Clamp(features.LowMid, 0, 1);
        double vocalWeight = features is null ? 0.52 : Math.Clamp(features.Mid + features.Presence, 0, 1);
        double trebleWeight = features is null ? 0.46 : Math.Clamp(features.Treble + features.Air, 0, 1);
        double brightness = features is null ? 0.48 : Math.Clamp((features.Presence * 0.45) + features.Treble + features.Air, 0, 1);
        double lowBodyWeight = bassWeight + warmthWeight;
        double highBodyWeight = (features is null ? 0.46 : features.Presence + features.Treble + features.Air);
        double balanceSpread = Math.Max(bassWeight, Math.Max(vocalWeight, trebleWeight)) - Math.Min(bassWeight, Math.Min(vocalWeight, trebleWeight));
        int energyPercent = (int)Math.Round(energy * 100);
        int brightnessPercent = (int)Math.Round(brightness * 100);

        EnergyLabel.Text = $"{energyPercent}%";
        BrightnessLabel.Text = $"{brightnessPercent}%";
        EnergyHintLabel.Text = energy switch
        {
            > 0.72 => "Âm lượng/động năng đang cao",
            < 0.20 => "Tín hiệu nhỏ hoặc đoạn yên tĩnh",
            _ => "Mức tín hiệu ổn định"
        };
        BrightnessHintLabel.Text = brightness switch
        {
            > 0.42 when highBodyWeight > lowBodyWeight * 0.85 => "Dải cao đang nhỉnh hơn",
            < 0.20 when lowBodyWeight > highBodyWeight * 1.25 => "Thiên ấm, ít dải cao",
            _ => "Dải cao ở mức vừa"
        };

        BassHintLabel.Text = DescribeBand(bassWeight, 0.18, 0.40, "Nhẹ", "Vừa", "Dày");
        VocalHintLabel.Text = DescribeBand(vocalWeight, 0.34, 0.62, "Lùi", "Rõ", "Nổi");
        TrebleHintLabel.Text = DescribeBand(trebleWeight, 0.12, 0.30, "Mềm", "Vừa", "Sáng");
        HighlightActiveSoundBand(bassWeight, vocalWeight, trebleWeight);
        AnimateMeter(EnergyMeter, energyPercent);
        AnimateMeter(BrightnessMeter, brightnessPercent);

        string badge = ResolveMoodBadge(energy, bassWeight, warmthWeight, vocalWeight, trebleWeight);
        MoodSummaryLabel.Text = badge switch
        {
            "SÁNG" => "Dải cao nhỉnh hơn phần còn lại",
            "BASS" => "Dải trầm/low-mid nhỉnh hơn",
            "VOCAL" => "Mid/presence là vùng rõ nhất",
            "MỀM" => "Tín hiệu thấp hoặc đoạn nghỉ",
            _ when balanceSpread < 0.16 => "Phân bố dải tương đối cân bằng",
            _ => "Không có dải nào áp đảo rõ rệt"
        };

        UpdateAutoEqVisualState();
    }

    private void UpdateAutoEqVisualState(bool force = false)
    {
        if (AutoEqStatusBrush is null) return;

        bool enabled = AutoEqCheckBox.IsChecked == true;
        if (!force && _initialized && enabled == _autoEqVisualEnabledState) return;
        _autoEqVisualEnabledState = enabled;
        Visibility manualVisibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        Visibility autoEqDetailVisibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        Color start = enabled ? Color.FromRgb(18, 59, 53) : Color.FromRgb(60, 31, 24);
        Color end = enabled ? Color.FromRgb(11, 16, 32) : Color.FromRgb(16, 24, 39);
        Color accent = enabled ? Color.FromRgb(25, 230, 193) : Color.FromRgb(255, 107, 107);
        Color foreground = enabled ? Color.FromRgb(215, 255, 245) : Color.FromRgb(255, 229, 218);

        ManualPresetPanel.Visibility = manualVisibility;
        ManualActionPanel.Visibility = manualVisibility;
        SoundPanel.Visibility = autoEqDetailVisibility;

        AutoEqStatusBrush.GradientStops[0].Color = start;
        AutoEqStatusBrush.GradientStops[1].Color = end;
        AutoEqStatusCard.BorderBrush = new SolidColorBrush(accent);
        AutoEqStatusDot.Fill = new SolidColorBrush(accent);
        AutoEqStatusGlow.Color = accent;
        AutoEqStatusGlow.Opacity = enabled ? 0.24 : 0.14;
        AutoEqStatusLabel.Foreground = new SolidColorBrush(foreground);
        AutoEqStatusLabel.Text = enabled ? "Đang bật - tự nghe và chọn preset APO" : "Đang tắt - dùng preset thủ công";
        SyncShortcutTextToVisibleUi();
    }

    private static string DescribeBand(double value, double lowThreshold, double highThreshold, string low, string middle, string high)
    {
        return value < lowThreshold ? low : value > highThreshold ? high : middle;
    }

    private static string ResolveMoodBadge(double energy, double bass, double warmth, double vocal, double treble)
    {
        if (energy < 0.20) return "MỀM";

        double max = Math.Max(bass, Math.Max(vocal, treble));
        if (max < 0.30 || max - Math.Min(bass, Math.Min(vocal, treble)) < 0.10) return "CÂN BẰNG";
        if (treble == max) return "SÁNG";
        if (bass == max || warmth > 0.28) return "BASS";
        return "VOCAL";
    }

    private static string TranslateAnalysisState(string value)
    {
        return value switch
        {
            "Balanced" => "cân bằng",
            "Boomy" => "bass/low-mid dày",
            "Vocal Recessed" => "vocal hơi lùi",
            "Harsh Treble" => "treble/presence gắt",
            "Quiet" => "nhẹ/yên tĩnh",
            _ => value
        };
    }

    private static string TranslateGenre(string value)
    {
        return value switch
        {
            "Unknown" => "chưa rõ",
            "Speech" => "giọng nói",
            "Acoustic" => "acoustic",
            "Electronic" => "electronic",
            "Rock" => "rock",
            "Pop" => "pop",
            "Classical" => "classical",
            _ => value
        };
    }

    private void HighlightActiveSoundBand(double bassWeight, double vocalWeight, double trebleWeight)
    {
        double max = Math.Max(bassWeight, Math.Max(vocalWeight, trebleWeight));
        SetBandCardState(BassBandCard, bassWeight == max, Color.FromRgb(255, 209, 102), Color.FromRgb(32, 48, 79));
        SetBandCardState(VocalBandCard, vocalWeight == max, Color.FromRgb(25, 230, 193), Color.FromRgb(7, 59, 76));
        SetBandCardState(TrebleBandCard, trebleWeight == max, Color.FromRgb(255, 179, 0), Color.FromRgb(199, 107, 28));
    }

    private static void SetBandCardState(Border card, bool isActive, System.Windows.Media.Color activeColor, System.Windows.Media.Color normalColor)
    {
        card.Background = new SolidColorBrush(isActive ? activeColor : normalColor);
        card.BorderBrush = new SolidColorBrush(isActive ? Color.FromRgb(248, 241, 227) : Color.FromRgb(32, 48, 79));
        card.BorderThickness = isActive ? new Thickness(1.4) : new Thickness(0);
        card.Effect = isActive
            ? new DropShadowEffect { Color = activeColor, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.42 }
            : null;
    }

    private static void AnimateMeter(System.Windows.Controls.ProgressBar meter, double value)
    {
        if (Math.Abs(meter.Value - value) < 2) return;

        var animation = new DoubleAnimation
        {
            To = value,
            Duration = TimeSpan.FromMilliseconds(360),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        meter.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, animation);
    }

    private void UpdateWaveBars(AudioFeatures? features)
    {
        double[] levels = features is null
            ? new[] { 0.24, 0.36, 0.48, 0.62, 0.48, 0.36, 0.24 }
            : new[] { features.SubBass, features.Bass, features.LowMid, features.Mid, features.Presence, features.Treble, features.Air };

        Border[] bars =
        {
            SubBassWaveBar,
            BassWaveBar,
            LowMidWaveBar,
            MidWaveBar,
            PresenceWaveBar,
            TrebleWaveBar,
            AirWaveBar
        };

        for (int i = 0; i < bars.Length; i++)
        {
            double pulse = features is null ? 0 : _waveRandom.NextDouble() * 0.035;
            _smoothWaveLevels[i] = Lerp(_smoothWaveLevels[i], Math.Clamp(levels[i] + pulse, 0.05, 1), 0.22);
            double height = 18 + _smoothWaveLevels[i] * 78;
            AnimateWaveBar(bars[i], height);
        }
    }

    private void UpdateInputScanner(IReadOnlyList<float> waveform)
    {
        if (InputScannerHost is null) return;

        double peak = 0;
        double sumSquares = 0;
        for (int i = 0; i < waveform.Count; i++)
        {
            double sample = Math.Abs(waveform[i]);
            peak = Math.Max(peak, sample);
            sumSquares += sample * sample;
        }

        double rms = waveform.Count == 0 ? 0 : Math.Sqrt(sumSquares / waveform.Count);
        double crestRatio = rms <= 0.0001 ? 1 : peak / rms;
        double crest = Math.Clamp((crestRatio - 1) / 7, 0, 1);

        _smoothInputPeak = Lerp(_smoothInputPeak, Math.Clamp(peak, 0, 1), 0.36);
        _smoothInputRms = Lerp(_smoothInputRms, Math.Clamp(rms * 2.6, 0, 1), 0.30);
        _smoothInputCrest = Lerp(_smoothInputCrest, crest, 0.24);

        WaveformStatusLabel.Text = waveform.Count == 0
            ? AutoEqCheckBox.IsChecked == true ? "ĐANG CHỜ" : "TẮT"
            : peak < 0.02 ? "IM LẶNG" : "LIVE";

        UpdateScannerBar(InputPeakMeter, InputPeakLabel, _smoothInputPeak, "P0");
        UpdateScannerBar(InputRmsMeter, InputRmsLabel, _smoothInputRms, "P0");
        UpdateScannerBar(InputCrestMeter, InputCrestLabel, _smoothInputCrest, "0.0x", 1 + _smoothInputCrest * 7);

        if (waveform.Count > 0)
        {
            UpdateSignalEqInteraction(_latestFeatures, peak);
            UpdateFlowBars(peak);
        }
    }

    private void UpdateScannerBar(Border meter, TextBlock label, double value, string format, double? labelValue = null)
    {
        double width = Math.Max(4, (InputScannerHost.ActualWidth - 92) * Math.Clamp(value, 0, 1));
        var animation = new DoubleAnimation
        {
            To = width,
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        meter.BeginAnimation(WidthProperty, animation);

        double displayValue = labelValue ?? value;
        label.Text = format == "P0" ? $"{Percent(displayValue)}%" : $"{displayValue:0.0}x";
    }

    private void UpdateSignalEqInteraction(AudioFeatures? features, double inputPeak)
    {
        if (SignalToEqFlowBar is null || EqResponseLabel is null || SignalEqLinkLabel is null) return;

        double bass = features is null ? 0 : Math.Clamp(features.SubBass + features.Bass, 0, 1);
        double vocal = features is null ? 0 : Math.Clamp(features.Mid + features.Presence, 0, 1);
        double treble = features is null ? 0 : Math.Clamp(features.Treble + features.Air, 0, 1);
        double drive = Math.Clamp(Math.Max(inputPeak, features?.Rms * 10 ?? 0), 0, 1);
        string activeBand = bass >= vocal && bass >= treble ? "BASS" : vocal >= treble ? "VOCAL" : "TREBLE";
        int inputPercent = Percent(drive);
        int bassPercent = Percent(bass);
        int vocalPercent = Percent(vocal);
        int treblePercent = Percent(treble);
        string action = ResolveEqAction(features, activeBand, bass, vocal, treble, drive);

        EqResponseLabel.Text = drive < 0.02 ? "WAITING INPUT" : $"INPUT → {activeBand}";
        SignalEqLinkLabel.Text = drive < 0.02
            ? "Chưa có tín hiệu đủ mạnh; EQ mixer đang chờ dữ liệu."
            : $"Nhìn bên phải: INPUT {inputPercent}% → {activeBand} mạnh nhất → EQ quyết định {action}.";

        _smoothSignalFlowWidth = Lerp(_smoothSignalFlowWidth, 24 + drive * 96, 0.16);
        AnimateFlowBar(SignalToEqFlowBar, _smoothSignalFlowWidth, 0.25 + drive * 0.7);
        UpdateDashboardCharts(features, drive);
    }

    private void UpdateDashboardCharts(AudioFeatures? features, double drive)
    {
        double[] levels = features is null
            ? new[] { 0.34, 0.48, 0.52, 0.58, 0.50, 0.44, 0.38 }
            : new[] { features.SubBass, features.Bass, features.LowMid, features.Mid, features.Presence, features.Treble, features.Air };

        for (int i = 0; i < levels.Length; i++)
        {
            _smoothDashboardLevels[i] = Lerp(_smoothDashboardLevels[i], Math.Clamp(levels[i], 0, 1), 0.18);
        }

        _smoothDashboardDrive = Lerp(_smoothDashboardDrive, Math.Clamp(drive, 0, 1), 0.16);
        UpdateEqCurve(_smoothDashboardLevels, features);

        _smoothBassMeter = Lerp(_smoothBassMeter, Math.Clamp(_smoothDashboardLevels[0] + _smoothDashboardLevels[1], 0, 1), 0.16);
        _smoothVocalMeter = Lerp(_smoothVocalMeter, Math.Clamp(_smoothDashboardLevels[3] + _smoothDashboardLevels[4], 0, 1), 0.16);
        _smoothTrebleMeter = Lerp(_smoothTrebleMeter, Math.Clamp(_smoothDashboardLevels[5] + _smoothDashboardLevels[6], 0, 1), 0.16);
        UpdateRadialMeter(BassRadialArc, BassPercentLabel, _smoothBassMeter);
        UpdateRadialMeter(VocalRadialArc, VocalPercentLabel, _smoothVocalMeter);
        UpdateRadialMeter(TrebleRadialArc, TreblePercentLabel, _smoothTrebleMeter);
        UpdateSoundLinkExplanation(features);
        UpdateFlowBars(_smoothDashboardDrive);
    }

    private void UpdateSoundLinkExplanation(AudioFeatures? features)
    {
        int bass = Percent(_smoothBassMeter);
        int vocal = Percent(_smoothVocalMeter);
        int treble = Percent(_smoothTrebleMeter);
        string strongest = bass >= vocal && bass >= treble ? "Bass" : vocal >= treble ? "Vocal" : "Treble";
        string mood = features is null ? "Cân bằng" : TranslateAnalysisState(features.State);
        string eqAction = ResolveEqAction(features, strongest.ToUpperInvariant(), _smoothBassMeter, _smoothVocalMeter, _smoothTrebleMeter, _smoothDashboardDrive);

        SoundLinkSummaryLabel.Text = $"Bass {bass} + Vocal {vocal} + Treble {treble} → {mood} → {eqAction}";
        SoundLinkDetailLabel.Text = features is null
            ? "Chưa có tín hiệu live; dashboard dùng mức mẫu để minh hoạ cách dữ liệu liên kết."
            : $"{strongest} đang nổi bật nhất; năng lượng tín hiệu {Percent(_smoothDashboardDrive)}% làm flow sáng hơn, EQ Curve đổi theo phổ tần hiện tại.";
    }

    private void UpdateEqCurve(double[] levels, AudioFeatures? features)
    {
        if (EqCurveLine is null) return;

        const double width = 260;
        const double height = 72;
        var points = new PointCollection();
        const int segments = 48;
        for (int i = 0; i < segments; i++)
        {
            double position = i / (double)(segments - 1) * (levels.Length - 1);
            int left = (int)Math.Floor(position);
            int right = Math.Min(levels.Length - 1, left + 1);
            double t = SmoothStep(position - left);
            double level = Lerp(levels[left], levels[right], t);
            double x = i / (double)(segments - 1) * width;
            double y = height - Math.Clamp(level, 0, 1) * height;
            points.Add(new Point(x, y));
        }

        EqCurveLine.Points = points;
        EqCurveLabel.Text = features is null ? "BALANCED" : TranslateAnalysisState(features.State).ToUpperInvariant();
    }

    private static double Lerp(double from, double to, double t)
    {
        return from + (to - from) * Math.Clamp(t, 0, 1);
    }

    private static double SmoothStep(double t)
    {
        double clamped = Math.Clamp(t, 0, 1);
        return clamped * clamped * (3 - 2 * clamped);
    }

    private static void UpdateRadialMeter(Path arc, TextBlock label, double value)
    {
        double clamped = Math.Clamp(value, 0.02, 0.99);
        double angle = -90 + clamped * 340;
        const double center = 31;
        const double radius = 25;
        double radians = angle * Math.PI / 180.0;
        Point end = new(center + Math.Cos(radians) * radius, center + Math.Sin(radians) * radius);
        bool large = clamped > 0.5;
        arc.Data = Geometry.Parse($"M 31 6 A 25 25 0 {(large ? 1 : 0)} 1 {end.X:F1} {end.Y:F1}");
        label.Text = Percent(value).ToString();
    }

    private void UpdateFlowBars(double drive)
    {
        double width = 18 + Math.Clamp(drive, 0, 1) * 72;
        AnimateFlowBar(InputAnalyzeFlowBar, width, 0.18 + drive * 0.8);
        AnimateFlowBar(AnalyzeEqFlowBar, width * 0.9, 0.22 + drive * 0.72);
        AnimateFlowBar(EqOutputFlowBar, width * 0.78, 0.20 + drive * 0.66);
    }

    private static void AnimateFlowBar(Border bar, double width, double opacity)
    {
        if (Math.Abs(bar.Width - width) < 1.5)
        {
            bar.Opacity = Math.Clamp(opacity, 0.12, 1);
            return;
        }

        var animation = new DoubleAnimation
        {
            To = width,
            Duration = TimeSpan.FromMilliseconds(520),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        bar.BeginAnimation(WidthProperty, animation);
        bar.Opacity = Math.Clamp(opacity, 0.12, 1);
    }

    private static string ResolveEqAction(AudioFeatures? features, string activeBand, double bass, double vocal, double treble, double drive)
    {
        if (features is null || drive < 0.02) return "Đang chờ tín hiệu";

        return features.State switch
        {
            "Boomy" => "CUT BASS / giảm ù",
            "Vocal Recessed" => "BOOST VOCAL / kéo giọng lên",
            "Harsh Treble" => "CUT TREBLE / giảm chói",
            "Quiet" => "BOOST NHẸ / tăng hiện diện",
            _ when Math.Max(bass, Math.Max(vocal, treble)) - Math.Min(bass, Math.Min(vocal, treble)) < 0.12 => "BALANCE / giữ cân bằng",
            _ => activeBand switch
            {
                "BASS" => "CONTROL BASS / giữ trầm gọn",
                "VOCAL" => "FOCUS VOCAL / giữ giọng rõ",
                _ => "SMOOTH TREBLE / làm dải cao mượt"
            }
        };
    }

    private static void AnimateWaveBar(Border bar, double height)
    {
        if (Math.Abs(bar.Height - height) < 2.2) return;

        var animation = new DoubleAnimation
        {
            To = height,
            Duration = TimeSpan.FromMilliseconds(540),
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        bar.BeginAnimation(HeightProperty, animation);

        double glow = Math.Clamp(height / 96, 0.25, 1);
        bar.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Color.FromRgb(242, 184, 75),
            BlurRadius = 8 + glow * 12,
            ShadowDepth = 0,
            Opacity = 0.16 + glow * 0.34
        };
    }

    private async Task RefreshNowPlayingAsync()
    {
        if (_refreshingNowPlaying) return;
        _refreshingNowPlaying = true;

        NowPlayingInfo info = await _nowPlayingService.GetCurrentAsync();
        try
        {
            string nowPlayingKey = $"{info.Title}|{info.Artist}|{info.Source}";
            if (!string.IsNullOrWhiteSpace(_lastNowPlayingKey) && nowPlayingKey != _lastNowPlayingKey)
            {
                AnimateRecordSwap();
            }

            _lastNowPlayingKey = nowPlayingKey;
            SetTextIfChanged(SongTitleLabel, info.Title);
            SetTextIfChanged(ArtistLabel, info.Artist);
            SetTextIfChanged(SourceLabel, $"Nguồn: {info.Source}");
            SetTextIfChanged(RecordStatusLabel, info.IsPlaying ? "ĐANG PHÁT" : "ĐÃ DỪNG");
            SetRecordSpinning(info.IsPlaying);
        }
        finally
        {
            _refreshingNowPlaying = false;
        }
    }

    private static void SetTextIfChanged(TextBlock textBlock, string value)
    {
        if (textBlock.Text != value)
        {
            textBlock.Text = value;
        }
    }

    private void SetRecordSpinning(bool shouldSpin)
    {
        if (_recordIsSpinning == shouldSpin) return;
        _recordIsSpinning = shouldSpin;

        if (shouldSpin)
        {
            var spin = new DoubleAnimation
            {
                From = RecordRotateTransform.Angle,
                To = RecordRotateTransform.Angle + 360,
                Duration = TimeSpan.FromSeconds(2.2),
                RepeatBehavior = RepeatBehavior.Forever
            };
            RecordRotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, spin);
        }
        else
        {
            RecordRotateTransform.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
        }
    }

    private void AnimateRecordSwap()
    {
        var shrink = new DoubleAnimation(1, 0.18, TimeSpan.FromMilliseconds(170))
        {
            AutoReverse = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        var fade = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(170))
        {
            AutoReverse = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        RecordSwapScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, shrink);
        RecordSwapScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, shrink);
        RecordDeck.BeginAnimation(OpacityProperty, fade);
    }

    private async void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        await RunMediaCommandAsync(() => _nowPlayingService.PlayPauseAsync(), "Play/Pause");
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        await RunMediaCommandAsync(() => _nowPlayingService.PreviousAsync(), "Previous");
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        await RunMediaCommandAsync(() => _nowPlayingService.NextAsync(), "Next");
    }

    private async void StopMediaButton_Click(object sender, RoutedEventArgs e)
    {
        await RunMediaCommandAsync(() => _nowPlayingService.StopAsync(), "Stop");
    }

    private async Task RunMediaCommandAsync(Func<Task<bool>> command, string name)
    {
        bool ok = await command();
        _logger.Info(ok ? $"Đã gửi lệnh media: {TranslateMediaCommand(name)}." : $"Không gửi được lệnh media: {TranslateMediaCommand(name)}.");
        await RefreshNowPlayingAsync();
    }

    private async Task ApplyPresetAsync(EqPreset preset, string reason)
    {
        AutoEqResult result = await _presetApplyService.ApplyAsync(preset, reason, _windowCts.Token);
        if (result.Success)
        {
            if (!string.Equals(result.Message, "Preset already applied.", StringComparison.OrdinalIgnoreCase))
            {
                _logger.Info($"Đã áp preset ''{preset.Name}'' vào Equalizer APO ({TranslateApplyReason(reason)}).");
            }

            return;
        }

        if (result.ErrorCode != "preset_apply_canceled")
        {
            _logger.Error(result.Message);
        }
    }

    private async Task DetectSystemAudioOutputAsync(bool applyBestPreset)
    {
        if (_windowCts.IsCancellationRequested) return;
        if (_detectingAudioOutput) return;
        _detectingAudioOutput = true;

        try
        {
            AudioOutputInfo outputInfo = await Task.Run(() => _systemVolumeService.GetAudioOutputInfo(), _windowCts.Token);
            _windowCts.Token.ThrowIfCancellationRequested();
            _audioOutputInfo = outputInfo;
            _currentOutputProfile = _outputProfileStore.GetOrCreate(outputInfo, NearWallCheckBox.IsChecked == true, NightModeCheckBox.IsChecked == true);
            string outputKey = $"{outputInfo.DefaultDeviceId}|{outputInfo.OutputSummary}";
            bool outputChanged = !string.Equals(_lastAudioOutputKey, outputKey, StringComparison.OrdinalIgnoreCase);
            _lastAudioOutputKey = outputKey;

            DeviceLabel.Text = FormatMainboardLabel();
            SoundCardLabel.Text = FormatSoundCardLabel();
            if (outputChanged)
            {
                _logger.Info($"Đã đồng bộ phần cứng: Mainboard {outputInfo.MainboardName}; Sound card {outputInfo.SoundCardName}.");
                _logger.Info($"Output âm thanh đang hoạt động: {outputInfo.OutputSummary}.");
                _logger.Info($"Profile AutoEQ output: {_currentOutputProfile.Name} ({_currentOutputProfile.DeviceType}) - {_currentOutputProfile.Reason}.");
            }
            UpdateSoundOutputList();

            if (!applyBestPreset || AutoEqCheckBox.IsChecked != true) return;

            string bestPresetName = _presetEngine.ChooseStartupPreset(
                outputInfo,
                NearWallCheckBox.IsChecked == true,
                NightModeCheckBox.IsChecked == true);

            if (string.Equals(_currentPresetName, bestPresetName, StringComparison.OrdinalIgnoreCase)) return;

            EqPreset bestPreset = _presetEngine.GetPreset(bestPresetName);
            _currentPresetName = bestPreset.Name;
            _updatingPresetFromAuto = true;
            PresetComboBox.SelectedItem = bestPreset.Name;
            _updatingPresetFromAuto = false;
            UpdateCurrentPresetLabel();
            await ApplyPresetAsync(bestPreset, "System output Auto EQ");
        }
        catch (OperationCanceledException) when (_windowCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.Error($"Không đọc được sound output Windows: {ex.Message}");
        }
        finally
        {
            _detectingAudioOutput = false;
        }
    }

    private string FormatMainboardLabel()
    {
        string? mainboard = _audioOutputInfo?.MainboardName;
        return string.IsNullOrWhiteSpace(mainboard) ? "Mainboard: đang đọc..." : $"Mainboard: {mainboard}";
    }

    private string FormatSoundCardLabel()
    {
        string? soundCard = _audioOutputInfo?.SoundCardName;
        string? version = _audioOutputInfo?.SoundCardVersion;
        if (string.IsNullOrWhiteSpace(soundCard)) return "Soundcard: đang đọc...";

        string versionText = string.IsNullOrWhiteSpace(version) || version == "Unknown version" ? "version chưa đọc được" : $"version {version}";
        return $"Soundcard: {soundCard} - {versionText}";
    }

    private void UpdateCurrentPresetLabel()
    {
        CurrentPresetLabel.Text = $"Preset đang dùng: {_currentPresetName}";
    }

    private void UpdateSoundOutputList()
    {
        if (SoundOutputsPanel is null) return;

        SoundOutputsPanel.Children.Clear();
        IReadOnlyList<string> outputs = _audioOutputInfo?.ActiveOutputNames ?? Array.Empty<string>();
        if (outputs.Count == 0 && _audioOutputInfo?.DefaultDeviceName is { Length: > 0 } fallbackName)
        {
            outputs = new[] { fallbackName };
        }

        if (outputs.Count == 0)
        {
            SoundOutputCountLabel.Text = "0 output";
            CurrentSoundOutputLabel.Text = "Chưa đọc được sound output";
            SoundOutputsPanel.Children.Add(CreateSoundOutputChip("Chưa đọc được sound output", false));
            return;
        }

        SoundOutputCountLabel.Text = $"{outputs.Count} output{(outputs.Count == 1 ? string.Empty : "s")}";
        CurrentSoundOutputLabel.Text = string.IsNullOrWhiteSpace(_audioOutputInfo?.DefaultDeviceName)
            ? outputs[0]
            : _audioOutputInfo.DefaultDeviceName;

        foreach (string output in outputs)
        {
            bool isActive = string.Equals(output, _audioOutputInfo?.DefaultDeviceName, StringComparison.OrdinalIgnoreCase);
            SoundOutputsPanel.Children.Add(CreateSoundOutputChip(output, isActive));
        }
    }

    private static Border CreateSoundOutputChip(string text, bool isActive)
    {
        Color background = isActive ? Color.FromRgb(255, 176, 0) : Color.FromRgb(16, 22, 36);
        Color foreground = isActive ? Color.FromRgb(16, 22, 36) : Color.FromRgb(248, 241, 227);
        Color border = isActive ? Color.FromRgb(248, 241, 227) : Color.FromRgb(32, 48, 79);

        return new Border
        {
            Background = new SolidColorBrush(background),
            BorderBrush = new SolidColorBrush(border),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(11, 7, 11, 7),
            Margin = new Thickness(0, 0, 8, 8),
            Effect = isActive ? new DropShadowEffect { Color = background, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.42 } : null,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new Ellipse
                    {
                        Width = 8,
                        Height = 8,
                        Fill = new SolidColorBrush(isActive ? Color.FromRgb(16, 22, 36) : Color.FromRgb(25, 230, 193)),
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = text,
                        Foreground = new SolidColorBrush(foreground),
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        TextWrapping = TextWrapping.Wrap,
                        MaxWidth = 210
                    }
                }
            }
        };
    }

    private void SyncSystemVolumeFromWindows()
    {
        try
        {
            int volume = _systemVolumeService.GetMasterVolumePercent();
            _syncingSystemVolume = true;
            _systemVolumePercent = volume;
            UpdateVolumeKnob(volume);
        }
        catch (Exception ex)
        {
            _logger.Error($"Không đọc được âm lượng Windows: {ex.Message}");
        }
        finally
        {
            _syncingSystemVolume = false;
        }
    }

    private void UpdateVolumeKnob(int volume)
    {
        int clampedVolume = Math.Clamp(volume, 0, 100);
        VolumeNeedleTransform.Angle = VolumeKnobMinAngle + clampedVolume * 2.7;
        VolumePercentLabel.Text = $"{clampedVolume}%";
        VolumeArc.Opacity = clampedVolume == 0 ? 0.18 : 1;
    }

    private static string FormatReadableAnalysisLog(AudioFeatures features)
    {
        string state = TranslateAnalysisState(features.State);
        string genre = TranslateGenre(features.GenreHint);
        int bass = Percent(features.SubBass + features.Bass);
        int vocal = Percent(features.Mid + features.Presence);
        int treble = Percent(features.Treble + features.Air);
        int energy = Percent(features.Rms * 14);

        return $"Phân tích âm thanh: {state}; thể loại {genre}; năng lượng {energy}%; bass {bass}%; vocal {vocal}%; treble {treble}%.";
    }

    private static int Percent(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 100);

    private static string TranslateApplyReason(string reason)
    {
        return reason switch
        {
            "Manual apply" => "người dùng bấm áp dụng",
            "Manual preset selected" => "người dùng chọn preset",
            "Shortcut apply" => "phím tắt Ctrl+Enter",
            "Auto EQ switched preset" => "Auto EQ tự chuyển theo âm thanh",
            "Native WASAPI Auto EQ" => "Auto EQ native WASAPI",
            "System output Auto EQ" => "tự tối ưu theo thiết bị output",
            _ => reason
        };
    }

    private static string TranslateMediaCommand(string name)
    {
        return name switch
        {
            "Play/Pause" or "Play/Pause shortcut" => "phát/tạm dừng",
            "Previous" or "Previous shortcut" => "bài trước",
            "Next" or "Next shortcut" => "bài tiếp",
            "Stop" => "dừng phát",
            _ => name
        };
    }

    private void AddLog(LogEntry entry)
    {
        LogListBox.Items.Add(entry);
        while (LogListBox.Items.Count > 300)
        {
            LogListBox.Items.RemoveAt(0);
        }
        LogListBox.ScrollIntoView(entry);
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _windowCts.Cancel();
        _nowPlayingTimer.Stop();
        _volumeSyncTimer.Stop();
        _audioOutputSyncTimer.Stop();
        _nativeAutoEqClient.StopMonitoring();
        _ = _nativeAutoEqClient.DisposeAsync();
        _captureService.Dispose();
        _presetApplyService.Dispose();
        _windowCts.Dispose();
        base.OnClosed(e);
    }
}













































































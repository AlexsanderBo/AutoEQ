using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;
using WoburnAutoEQ.Models;
using WoburnAutoEQ.Services;

namespace WoburnAutoEQ;

public partial class MainWindow : Window
{
    private readonly AppLogger _logger = new();
    private readonly AudioCaptureService _captureService = new();
    private readonly PresetEngine _presetEngine = new();
    private readonly EqualizerApoManager _apoManager = new();
    private readonly NowPlayingService _nowPlayingService = new();
    private readonly SystemVolumeService _systemVolumeService = new();
    private readonly DispatcherTimer _nowPlayingTimer = new();
    private readonly DispatcherTimer _volumeSyncTimer = new();
    private readonly DispatcherTimer _audioOutputSyncTimer = new();
    private readonly Random _waveRandom = new();
    private AudioOutputInfo? _audioOutputInfo;
    private string _lastAudioOutputKey = string.Empty;
    private string _currentPresetName = "Universal Warm Balance";
    private bool _initialized;
    private bool _updatingPresetFromAuto;
    private bool _syncingSystemVolume;
    private bool _draggingVolumeKnob;
    private int _systemVolumePercent;
    private int? _pendingVolumePercent;
    private DateTime _lastVolumeCommitUtc = DateTime.MinValue;
    private string _lastNowPlayingKey = string.Empty;
    private bool _refreshingNowPlaying;
    private bool _recordIsSpinning;
    private bool _isMonitoring;
    private bool _detectingAudioOutput;
    private DateTime _lastFeatureUiUpdateUtc = DateTime.MinValue;
    private DateTime _lastFeatureLogUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _presetApplySemaphore = new(1, 1);
    private string _lastAppliedPresetText = string.Empty;
    private AudioFeatures? _latestFeatures;
    private bool _featureUiQueued;
    private bool _autoEqVisualEnabledState;
    private DateTime _lastAutoEqSwitchUtc = DateTime.MinValue;
    private string _lastAppliedPresetName = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        SyncShortcutTextToVisibleUi();
        PresetComboBox.ItemsSource = _presetEngine.GetPresetNames();
        PresetComboBox.SelectedItem = _currentPresetName;
        _ = DetectSystemAudioOutputAsync(applyBestPreset: true);
        SyncSystemVolumeFromWindows();
        _initialized = true;

        _logger.MessageLogged += (_, message) => Dispatcher.Invoke(() => AddLog(message));
        _captureService.FeaturesAvailable += CaptureService_FeaturesAvailable;
        _captureService.ErrorOccurred += (_, message) => _logger.Error(message);
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

        _logger.Info("Ready. Audio stays local; no cloud or external API is used.");
        UpdateWaveBars(null);
        UpdateAudioMood(null);
        UpdateSoundOutputList();
        UpdateAutoEqVisualState();
        SetAutoEqEnabled(AutoEqCheckBox.IsChecked == true);
        if (!_apoManager.IsInstalled())
        {
            _logger.Error("Equalizer APO config folder was not found. Install Equalizer APO or check the path.");
        }
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

    private void VolumeKnob_MouseMove(object sender, MouseEventArgs e)
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

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
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

    private void SetVolumeFromKnobPoint(Point point)
    {
        double dx = point.X - 56;
        double dy = point.Y - 56;
        double angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        double knobAngle = angle < -135 ? angle + 360 : angle;

        // Hardware-style 270-degree travel: -135deg is 0%, +135deg is 100%.
        knobAngle = Math.Clamp(knobAngle, -135, 135);
        int volume = (int)Math.Round((knobAngle + 135) / 270 * 100);
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

    private async Task ProcessLatestFeaturesAsync()
    {
        AudioFeatures? features = _latestFeatures;
        _latestFeatures = null;
        _featureUiQueued = false;
        if (features is null) return;

        bool refreshVisuals = DateTime.UtcNow - _lastFeatureUiUpdateUtc >= TimeSpan.FromMilliseconds(850);
        if (refreshVisuals)
        {
            _lastFeatureUiUpdateUtc = DateTime.UtcNow;
            AnalysisStateLabel.Text = $"Trạng thái: {TranslateAnalysisState(features.State)} | RMS {features.Rms:F4}";
            GenreLabel.Text = $"Thể loại: {TranslateGenre(features.GenreHint)}";
            UpdateWaveBars(features);
            UpdateAudioMood(features);
        }

        bool writeAnalysisLog = DateTime.UtcNow - _lastFeatureLogUtc >= TimeSpan.FromSeconds(5);
        if (writeAnalysisLog)
        {
            _lastFeatureLogUtc = DateTime.UtcNow;
            _logger.Info($"State={features.State}, bass={features.Bass:P0}, low_mid={features.LowMid:P0}, mid={features.Mid:P0}, presence={features.Presence:P0}, treble={features.Treble:P0}, air={features.Air:P0}");
        }

        PresetEngine.PresetDecision decision = _presetEngine.EvaluateDynamicAutoEq(
            features,
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
            UpdateCurrentPresetLabel();
            UpdateAudioMood(features);
            await ApplyPresetAsync(requested, "Auto EQ switched preset");
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

    private void UpdateAutoEqVisualState()
    {
        if (AutoEqStatusBrush is null) return;

        bool enabled = AutoEqCheckBox.IsChecked == true;
        if (_initialized && enabled == _autoEqVisualEnabledState) return;
        _autoEqVisualEnabledState = enabled;
        Visibility manualVisibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        Color start = enabled ? Color.FromRgb(18, 59, 53) : Color.FromRgb(60, 31, 24);
        Color end = enabled ? Color.FromRgb(11, 16, 32) : Color.FromRgb(16, 24, 39);
        Color accent = enabled ? Color.FromRgb(25, 230, 193) : Color.FromRgb(255, 107, 107);
        Color foreground = enabled ? Color.FromRgb(215, 255, 245) : Color.FromRgb(255, 229, 218);

        ManualPresetPanel.Visibility = manualVisibility;
        ManualActionPanel.Visibility = manualVisibility;

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

    private static void SetBandCardState(Border card, bool isActive, Color activeColor, Color normalColor)
    {
        card.Background = new SolidColorBrush(isActive ? activeColor : normalColor);
        card.BorderBrush = new SolidColorBrush(isActive ? Color.FromRgb(248, 241, 227) : Color.FromRgb(32, 48, 79));
        card.BorderThickness = isActive ? new Thickness(1.4) : new Thickness(0);
        card.Effect = isActive
            ? new DropShadowEffect { Color = activeColor, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.42 }
            : null;
    }

    private static void AnimateMeter(ProgressBar meter, double value)
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
            double pulse = features is null ? 0 : _waveRandom.NextDouble() * 0.12;
            double height = 18 + Math.Clamp(levels[i] + pulse, 0.05, 1) * 78;
            AnimateWaveBar(bars[i], height);
        }
    }

    private static void AnimateWaveBar(Border bar, double height)
    {
        if (Math.Abs(bar.Height - height) < 3) return;

        var animation = new DoubleAnimation
        {
            To = height,
            Duration = TimeSpan.FromMilliseconds(420),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
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
        _logger.Info(ok ? $"Media command: {name}." : $"Media command not available: {name}.");
        await RefreshNowPlayingAsync();
    }

    private async Task ApplyPresetAsync(EqPreset preset, string reason)
    {
        if (!preset.IsDynamic && string.Equals(_lastAppliedPresetName, preset.Name, StringComparison.OrdinalIgnoreCase)) return;

        await _presetApplySemaphore.WaitAsync();
        try
        {
            if (!preset.IsDynamic && string.Equals(_lastAppliedPresetName, preset.Name, StringComparison.OrdinalIgnoreCase)) return;
            await ApplyPresetWithSmoothTransitionAsync(preset);
            _lastAppliedPresetName = preset.Name;
            _lastAppliedPresetText = preset.EqualizerApoText;
            _logger.Info($"{reason}: applied '{preset.Name}' to Equalizer APO.");
        }
        catch (Exception ex)
        {
            _logger.Error(ex.Message);
        }
        finally
        {
            _presetApplySemaphore.Release();
        }
    }

    private async Task ApplyPresetWithSmoothTransitionAsync(EqPreset preset)
    {
        if (string.IsNullOrWhiteSpace(_lastAppliedPresetText))
        {
            await _apoManager.ApplyPresetAsync(preset, "Initial apply");
            return;
        }

        EqCurve from = EqCurve.Parse(_lastAppliedPresetText);
        EqCurve to = EqCurve.Parse(preset.EqualizerApoText);

        const int steps = 24;
        const int delayMs = 70;
        for (int step = 1; step <= steps; step++)
        {
            double t = SmoothStep(step / (double)steps);
            string text = EqCurve.Interpolate(from, to, t).ToEqualizerApoText();
            await _apoManager.ApplyPresetAsync(new EqPreset
            {
                Name = $"{preset.Name} transition",
                EqualizerApoText = text,
                IsDynamic = preset.IsDynamic
            }, $"Smooth transition {step}/{steps}");
            await Task.Delay(delayMs);
        }
    }

    private static double SmoothStep(double t) => t * t * (3 - 2 * t);

    private sealed class EqCurve
    {
        private static readonly Regex PreampRegex = new(@"Preamp:\s*(?<gain>[-+]?\d+(?:\.\d+)?)\s*dB", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FilterRegex = new(@"Filter:\s*ON\s+PK\s+Fc\s+(?<freq>\d+)\s+Hz\s+Gain\s+(?<gain>[-+]?\d+(?:\.\d+)?)\s+dB\s+Q\s+(?<q>[-+]?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public double Preamp { get; private set; }
        public Dictionary<int, EqBand> Bands { get; init; } = new();

        public static EqCurve Parse(string text)
        {
            var curve = new EqCurve();
            Match preampMatch = PreampRegex.Match(text);
            if (preampMatch.Success)
            {
                curve.Preamp = ParseInvariant(preampMatch.Groups["gain"].Value);
            }

            foreach (Match match in FilterRegex.Matches(text))
            {
                int frequency = int.Parse(match.Groups["freq"].Value, System.Globalization.CultureInfo.InvariantCulture);
                curve.Bands[frequency] = new EqBand(
                    frequency,
                    ParseInvariant(match.Groups["gain"].Value),
                    ParseInvariant(match.Groups["q"].Value));
            }

            return curve;
        }

        public static EqCurve Interpolate(EqCurve from, EqCurve to, double t)
        {
            var result = new EqCurve
            {
                Preamp = Lerp(from.Preamp, to.Preamp, t)
            };

            foreach (int frequency in from.Bands.Keys.Concat(to.Bands.Keys).Distinct().OrderBy(freq => freq))
            {
                EqBand? start = from.Bands.GetValueOrDefault(frequency);
                EqBand? end = to.Bands.GetValueOrDefault(frequency);
                double gain = Lerp(start?.GainDb ?? 0, end?.GainDb ?? 0, t);
                double q = Lerp(start?.Q ?? end?.Q ?? 1.0, end?.Q ?? start?.Q ?? 1.0, t);
                if (Math.Abs(gain) < 0.05) continue;

                result.Bands[frequency] = new EqBand(frequency, gain, q);
            }

            return result;
        }

        public string ToEqualizerApoText()
        {
            var lines = new List<string> { $"Preamp: {FormatInvariant(Preamp)} dB" };
            lines.AddRange(Bands.Values
                .OrderBy(band => band.FrequencyHz)
                .Select(band => $"Filter: ON PK Fc {band.FrequencyHz} Hz Gain {FormatInvariant(band.GainDb)} dB Q {FormatInvariant(band.Q)}"));

            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        private static double Lerp(double from, double to, double t) => from + ((to - from) * t);

        private static double ParseInvariant(string value) => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

        private static string FormatInvariant(double value) => value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed record EqBand(int FrequencyHz, double GainDb, double Q);

    private async Task DetectSystemAudioOutputAsync(bool applyBestPreset)
    {
        if (_detectingAudioOutput) return;
        _detectingAudioOutput = true;

        try
        {
            AudioOutputInfo outputInfo = await Task.Run(() => _systemVolumeService.GetAudioOutputInfo());
            _audioOutputInfo = outputInfo;
            string outputKey = $"{outputInfo.DefaultDeviceId}|{outputInfo.OutputSummary}";
            bool outputChanged = !string.Equals(_lastAudioOutputKey, outputKey, StringComparison.OrdinalIgnoreCase);
            _lastAudioOutputKey = outputKey;

            DeviceLabel.Text = FormatMainboardLabel();
            SoundCardLabel.Text = FormatSoundCardLabel();
            if (outputChanged)
            {
                _logger.Info($"Tự đồng bộ phần cứng: mainboard={outputInfo.MainboardName}; sound card={outputInfo.SoundCardName}.");
                _logger.Info($"Sound outputs đang active: {outputInfo.OutputSummary}.");
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
        catch (Exception ex)
        {
            _logger.Error($"Could not read Windows sound output: {ex.Message}");
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
            SoundOutputsPanel.Children.Add(CreateSoundOutputChip("Chưa đọc được sound output", false));
            return;
        }

        SoundOutputCountLabel.Text = $"{outputs.Count} output{(outputs.Count == 1 ? string.Empty : "s")}";

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
            _logger.Error($"Could not read Windows volume: {ex.Message}");
        }
        finally
        {
            _syncingSystemVolume = false;
        }
    }

    private void UpdateVolumeKnob(int volume)
    {
        VolumeNeedleTransform.Angle = -135 + Math.Clamp(volume, 0, 100) * 2.7;
    }

    private void AddLog(string message)
    {
        LogListBox.Items.Add(message);
        while (LogListBox.Items.Count > 300)
        {
            LogListBox.Items.RemoveAt(0);
        }
        LogListBox.ScrollIntoView(message);
    }

    protected override void OnClosed(EventArgs e)
    {
        _nowPlayingTimer.Stop();
        _volumeSyncTimer.Stop();
        _audioOutputSyncTimer.Stop();
        _captureService.Dispose();
        base.OnClosed(e);
    }
}






































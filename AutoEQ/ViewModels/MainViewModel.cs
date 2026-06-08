using System.Globalization;
using System.Text;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia.Threading;
using AutoEQ.Services;

namespace AutoEQ.ViewModels;

public sealed class BandVm : INotifyPropertyChanged
{
    private double _value;

    public BandVm(string name, string description, double value = 0)
    {
        Name = name;
        Description = description;
        _value = value;
    }

    public string Name { get; }
    public string Description { get; }

    public double Value
    {
        get => _value;
        set
        {
            double clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(_value - clamped) < 0.001) return;
            _value = clamped;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SynchronizationContext? _uiContext;
    private DateTime _lastVisualUpdateUtc = DateTime.MinValue;
    private DateTime _lastMoodUpdateUtc = DateTime.MinValue;
    private string _lastSignalLogKey = string.Empty;
    private DateTime _lastSignalLogUtc = DateTime.MinValue;

    private bool _autoEqEnabled = true;
    private string _currentPresetName = "Universal Warm Balance";
    private string _analysisState = "cân bằng";
    private double _confidence;
    private double _rms;
    private double _crestDb;
    private int _energyPercent = 62;
    private int _brightnessPercent = 48;
    private double _bassLevel;
    private double _vocalLevel;
    private double _trebleLevel;
    private int _volumePercent;
    private bool _isMuted;
    private string _nowPlayingTitle = "Chưa phát nhạc";
    private string _nowPlayingArtist = "";
    private string _nowPlayingSource = "Local";
    private bool _isPlaying = true;
    private string _outputDeviceName = "đang đọc...";
    private string _mainboardName = "đang đọc...";
    private string _soundCardName = "đang đọc...";
    private double _adjustBassDb;
    private double _adjustVocalDb;
    private double _adjustTrebleDb;
    private DateTime _lastAutoUpdateUtc = DateTime.UtcNow;
    private bool _nearWallMode;
    private bool _nightMode;
    private bool _loudnessCompEnabled = false;
    private const double WaveformWidth = 720;
    private const double WaveformHeight = 150;
    private const double WaveformCenterY = 75;
    private const double WaveformSafeX = 46;
    private const double WaveformSafeY = 14;
    private const double WaveformSafeWidth = 628;
    private const double WaveformSafeHeight = 100;
    private string _eqCurvePathData = "M0,72 C50,70 70,46 110,44 C150,42 170,58 200,60 C240,63 260,40 300,46 C320,49 330,54 340,55";
    private string _waveformPathData = "M46,75 L674,75";
    private string _waveformFillPathData = "M46,75 L674,75 L674,75 L46,75 Z"; // flat seed at center until first frame
    private string _audioFormatText = "đang đọc... · APO";
    private bool _isCaptureHealthy = true;
    private string _captureStatusText = "ĐANG KHỞI ĐỘNG";
    private bool _isApoInstalled = true;
    private double _analysisRateSeconds;
    private DateTime _nextAnalysisUtc = DateTime.UtcNow.AddSeconds(AutoEQ.Config.AppConfig.AnalysisIntervalSeconds);
    private readonly DispatcherTimer _countdownTimer;

    public MainViewModel()
    {
        _uiContext = SynchronizationContext.Current;

        // Đếm ngược tới lần phân tích kế; tick mỗi giây để cập nhật AutoUpdateStatus.
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => OnPropertyChanged(nameof(AutoUpdateStatus));
        _countdownTimer.Start();

        Bands = new ObservableCollection<BandVm>
        {
            new("Sub", "20–60 Hz"),
            new("Bass", "60–250 Hz"),
            new("LowMid", "250–500 Hz"),
            new("Mid", "500 Hz–2 kHz"),
            new("Presence", "2–4 kHz"),
            new("Treble", "4–10 kHz"),
            new("Air", "10–16 kHz")
        };

        SignalLog = new ObservableCollection<LogEntry>();
        PresetNames = new ObservableCollection<string>();

        ApplyPresetCommand = new RelayCommand(_ => ApplyPresetRequested?.Invoke(CurrentPresetName));

        // Nút play/pause tự quản trạng thái: lật IsPlaying mỗi lần bấm, không bị poll ghi đè.
        PlayPauseCommand = new RelayCommand(_ =>
        {
            IsPlaying = !IsPlaying;
            PlayPauseRequested?.Invoke();
        });
        NextCommand = new RelayCommand(_ => NextRequested?.Invoke());
        PrevCommand = new RelayCommand(_ => PrevRequested?.Invoke());
    }

    public ObservableCollection<BandVm> Bands { get; }
    public ObservableCollection<LogEntry> SignalLog { get; }
    public ObservableCollection<string> PresetNames { get; }
    public ICommand ApplyPresetCommand { get; }

    public ICommand PlayPauseCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PrevCommand { get; }
    public event Action<bool>? AutoEqChanged;
    public event Action<string>? ApplyPresetRequested;

    public event Action? PlayPauseRequested;
    public event Action? NextRequested;
    public event Action? PrevRequested;
    public event Action? VoicingOptionsChanged;

    public bool AutoEqEnabled { get => _autoEqEnabled; set => Set(ref _autoEqEnabled, value, after: () => AutoEqChanged?.Invoke(value)); }
    public string CurrentPresetName { get => _currentPresetName; set => Set(ref _currentPresetName, value); }
    public string AnalysisState { get => _analysisState; set => Set(ref _analysisState, value); }
    public double Confidence { get => _confidence; set => Set(ref _confidence, value); }
    public double Rms { get => _rms; set => Set(ref _rms, value); }
    public double CrestDb { get => _crestDb; set => Set(ref _crestDb, value); }
    public int EnergyPercent { get => _energyPercent; set => Set(ref _energyPercent, value); }
    public int BrightnessPercent { get => _brightnessPercent; set => Set(ref _brightnessPercent, value); }
    public double BassLevel { get => _bassLevel; set => Set(ref _bassLevel, value); }
    public double VocalLevel { get => _vocalLevel; set => Set(ref _vocalLevel, value); }
    public double TrebleLevel { get => _trebleLevel; set => Set(ref _trebleLevel, value); }
    public int VolumePercent { get => _volumePercent; set => Set(ref _volumePercent, Math.Clamp(value, 0, 100)); }
    public bool IsMuted { get => _isMuted; set => Set(ref _isMuted, value); }
    public string NowPlayingTitle { get => _nowPlayingTitle; set => Set(ref _nowPlayingTitle, value); }
    public string NowPlayingArtist { get => _nowPlayingArtist; set => Set(ref _nowPlayingArtist, value); }
    public string NowPlayingSource { get => _nowPlayingSource; set => Set(ref _nowPlayingSource, value); }
    public bool IsPlaying { get => _isPlaying; set => Set(ref _isPlaying, value); }
    public string OutputDeviceName { get => _outputDeviceName; set => Set(ref _outputDeviceName, value); }
    public string MainboardName { get => _mainboardName; set => Set(ref _mainboardName, value); }
    public string SoundCardName { get => _soundCardName; set => Set(ref _soundCardName, value); }
    public double AdjustBassDb { get => _adjustBassDb; set => Set(ref _adjustBassDb, value); }
    public double AdjustVocalDb { get => _adjustVocalDb; set => Set(ref _adjustVocalDb, value); }
    public double AdjustTrebleDb { get => _adjustTrebleDb; set => Set(ref _adjustTrebleDb, value); }
    public bool NearWallMode { get => _nearWallMode; set => Set(ref _nearWallMode, value, after: () => VoicingOptionsChanged?.Invoke()); }
    public bool NightMode { get => _nightMode; set => Set(ref _nightMode, value, after: () => VoicingOptionsChanged?.Invoke()); }
    public bool LoudnessCompEnabled { get => _loudnessCompEnabled; set => Set(ref _loudnessCompEnabled, value, after: () => VoicingOptionsChanged?.Invoke()); }
    public string EqCurvePathData { get => _eqCurvePathData; set => Set(ref _eqCurvePathData, value); }
    public string WaveformPathData { get => _waveformPathData; set => Set(ref _waveformPathData, value); }

    // Path đóng (theo đường sóng rồi kéo xuống đáy) để fill khối gradient mượt.
    public string WaveformFillPathData { get => _waveformFillPathData; set => Set(ref _waveformFillPathData, value); }

    // Định dạng audio thật (sample rate · bit depth · trạng thái APO) — bind ở header.
    public string AudioFormatText { get => _audioFormatText; set => Set(ref _audioFormatText, value); }

    // Chấm trạng thái capture: xanh = sống, đỏ = lỗi/đứt.
    public bool IsCaptureHealthy { get => _isCaptureHealthy; set => Set(ref _isCaptureHealthy, value); }
    public string CaptureStatusText { get => _captureStatusText; set => Set(ref _captureStatusText, value); }

    // Cảnh báo khi Equalizer APO chưa cài (EQ không áp được).
    public bool IsApoInstalled { get => _isApoInstalled; set { if (Set(ref _isApoInstalled, value)) OnPropertyChanged(nameof(ApoWarningVisible)); } }
    public bool ApoWarningVisible => !_isApoInstalled;

    // "7 DẢI · 5S" build từ số band thật + chu kỳ cấu hình.
    public string BandAndIntervalText => $"{Bands.Count} DẢI · {AutoEQ.Config.AppConfig.AnalysisIntervalSeconds}S";

    public DateTime LastAutoUpdateUtc { get => _lastAutoUpdateUtc; set { if (Set(ref _lastAutoUpdateUtc, value)) OnPropertyChanged(nameof(AutoUpdateStatus)); } }

    // Nhịp phân tích THỰC đo giữa 2 lần OnFeatures (giây/chu kỳ).
    public double AnalysisRateSeconds { get => _analysisRateSeconds; set { if (Set(ref _analysisRateSeconds, value)) OnPropertyChanged(nameof(AutoUpdateStatus)); } }

    public string AutoUpdateStatus
    {
        get
        {
            // Đếm ngược số giây tới lần phân tích kế (làm tròn lên), kẹp trong [0, interval].
            double remaining = (_nextAnalysisUtc - DateTime.UtcNow).TotalSeconds;
            int secs = (int)Math.Ceiling(Math.Clamp(remaining, 0, AutoEQ.Config.AppConfig.AnalysisIntervalSeconds));
            return secs > 0 ? $"phân tích sau {secs}s" : "đang phân tích…";
        }
    }

    public void SetAudioFormat(int sampleRate, int bitsPerSample, bool apoOk) => Post(() =>
    {
        double khz = sampleRate / 1000.0;
        string khzText = khz % 1 == 0 ? $"{khz:0}" : $"{khz:0.0}";
        string apo = apoOk ? "APO" : "APO ✗";
        AudioFormatText = $"{khzText} kHz · {bitsPerSample}-bit · {apo}";
    });

    public void SetCaptureStatus(bool healthy, string text) => Post(() =>
    {
        IsCaptureHealthy = healthy;
        CaptureStatusText = text;
    });

    public void SetAnalysisRate(double seconds) => Post(() =>
    {
        AnalysisRateSeconds = seconds;
        // Mỗi lần frame phân tích mới về, đặt lại mốc đếm ngược cho chu kỳ kế.
        _nextAnalysisUtc = DateTime.UtcNow.AddSeconds(AutoEQ.Config.AppConfig.AnalysisIntervalSeconds);
        OnPropertyChanged(nameof(AutoUpdateStatus));
    });


    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetVolumeFromSystem(int volume, bool muted) => Post(() =>
    {
        VolumePercent = Math.Clamp(volume, 0, 100);
        IsMuted = muted;
    });

    public void UpdateFeatureFrame(string state, double confidence, double rms, double crestDb, double[] bands)
    {
        if (DateTime.UtcNow - _lastVisualUpdateUtc < TimeSpan.FromMilliseconds(33)) return;
        _lastVisualUpdateUtc = DateTime.UtcNow;
        Post(() =>
        {
            AnalysisState = state;
            Confidence = confidence;
            Rms = rms;
            CrestDb = crestDb;
            for (int i = 0; i < Bands.Count && i < bands.Length; i++)
            {
                // Làm mượt phổ ngay trong VM để UI không giật khi frame audio dao động mạnh.
                double target = Math.Clamp(bands[i], 0, 1);
                double current = Bands[i].Value;
                Bands[i].Value = current + ((target - current) * 0.35);
            }
        });
    }

    public void UpdateMood(int energyPercent, int brightnessPercent, double bass, double vocal, double treble)
    {
        if (DateTime.UtcNow - _lastMoodUpdateUtc < TimeSpan.FromMilliseconds(100)) return;
        _lastMoodUpdateUtc = DateTime.UtcNow;
        Post(() =>
        {
            EnergyPercent = energyPercent;
            BrightnessPercent = brightnessPercent;
            BassLevel = bass;
            VocalLevel = vocal;
            TrebleLevel = treble;
        });
    }

    public void UpdateWaveform(float[] samples)
    {
        if (samples.Length == 0) return;

        int pointCount = samples.Length;
        double maxAmp = WaveformSafeHeight / 2.0;
        // Đối xứng quanh trục giữa (kiểu DAW): trên = center-amp, dưới = center+amp.
        // Dùng |sample| làm biên độ envelope cho hai nửa cân nhau, đầy đặn fit bảng.
        var top = new string[pointCount];
        var bottom = new string[pointCount];
        for (int i = 0; i < pointCount; i++)
        {
            double x = WaveformSafeX + (i * WaveformSafeWidth / Math.Max(1, pointCount - 1));
            double amp = Clamp(Math.Abs(samples[i]), 0.0, 1.0) * maxAmp;
            double yTop = WaveformCenterY - amp;
            double yBot = WaveformCenterY + amp;
            top[i] = string.Format(CultureInfo.InvariantCulture, "{0:0.#},{1:0.#}", x, yTop);
            bottom[i] = string.Format(CultureInfo.InvariantCulture, "{0:0.#},{1:0.#}", x, yBot);
        }

        // Viền trên (trái→phải) + viền dưới (phải→trái) khép thành envelope kín để tô gradient.
        var fillBuilder = new StringBuilder(pointCount * 18);
        fillBuilder.Append('M').Append(top[0]);
        for (int i = 1; i < pointCount; i++) fillBuilder.Append(" L").Append(top[i]);
        for (int i = pointCount - 1; i >= 0; i--) fillBuilder.Append(" L").Append(bottom[i]);
        fillBuilder.Append(" Z");
        string fill = fillBuilder.ToString();

        // Đường sáng chạy dọc mép trên của envelope.
        string line = "M" + string.Join(" L", top);

        Post(() =>
        {
            WaveformPathData = line;
            WaveformFillPathData = fill;
        });

    }

    private static double Clamp(double value, double min, double max)
    {
        return value < min ? min : value > max ? max : value;
    }

    public void UpdateAdjustmentSummary(double bassDb, double vocalDb, double trebleDb)
    {
        Post(() =>
        {
            AdjustBassDb = bassDb;
            AdjustVocalDb = vocalDb;
            AdjustTrebleDb = trebleDb;
            LastAutoUpdateUtc = DateTime.UtcNow;
        });
    }

    public void SetPresetNames(IEnumerable<string> names) => Post(() =>
    {
        PresetNames.Clear();
        foreach (string name in names) PresetNames.Add(name);
    });

    public void AddSignalLog(LogEntry entry) => Post(() =>
    {
        string message = ShortenSignalMessage(entry.DisplayMessage);
        string key = NormalizeSignalKey(message);
        DateTime now = DateTime.UtcNow;

        // Bỏ dòng lặp gần nhau để đường tín hiệu không ghi đè thông tin cùng nghĩa.
        if (key == _lastSignalLogKey && now - _lastSignalLogUtc < TimeSpan.FromSeconds(8)) return;

        _lastSignalLogKey = key;
        _lastSignalLogUtc = now;
        SignalLog.Insert(0, new LogEntry(entry.Time, entry.Level, message));
        while (SignalLog.Count > 24) SignalLog.RemoveAt(SignalLog.Count - 1);
    });

    public void SetNowPlaying(NowPlayingInfo info) => Post(() =>
    {
        NowPlayingTitle = info.Title;
        NowPlayingArtist = info.Artist;
        NowPlayingSource = info.Source;
        // KHÔNG để poll ghi đè IsPlaying: audio session nhảy Active/Inactive theo có tiếng/im lặng
        // sẽ làm icon play/pause tự đổi. Trạng thái phát/dừng chỉ do nút bấm quyết định.
    });

    public void SetEqCurvePath(string pathData) => Post(() => EqCurvePathData = pathData);

    public void Post(Action action)
    {
        if (_uiContext is not null)
        {
            _uiContext.Post(_ => action(), null);
            return;
        }

        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action, DispatcherPriority.Background);
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null, Action? after = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        after?.Invoke();
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private static string ShortenSignalMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "OK";

        string text = message.Replace("AutoEQ", "AEQ", StringComparison.OrdinalIgnoreCase)
            .Replace("orchestrator", "dịch vụ", StringComparison.OrdinalIgnoreCase)
            .Replace("native snapshot", "native OK", StringComparison.OrdinalIgnoreCase)
            .Replace("đã khởi động", "sẵn sàng", StringComparison.OrdinalIgnoreCase)
            .Replace("đang theo dõi", "theo dõi", StringComparison.OrdinalIgnoreCase);

        int presetIndex = text.IndexOf("[Preset]", StringComparison.OrdinalIgnoreCase);
        if (presetIndex >= 0)
        {
            text = text[(presetIndex + "[Preset]".Length)..].Trim();
            int reasonIndex = text.IndexOf(" • ", StringComparison.Ordinal);
            if (reasonIndex >= 0) text = text[..reasonIndex].Trim();
            text = "Preset: " + text;
        }

        text = text.Replace("  ", " ").Trim();
        return text.Length <= 72 ? text : text[..69] + "...";
    }

    private static string NormalizeSignalKey(string message)
    {
        string key = message.ToLowerInvariant();
        int colon = key.IndexOf(':');
        if (colon > 0) key = key[..colon];
        return key.Trim();
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;
    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) { _execute = execute; _canExecute = canExecute; }
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}

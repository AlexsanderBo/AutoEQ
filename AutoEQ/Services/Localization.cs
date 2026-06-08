using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;

namespace AutoEQ.Services;

public enum AppLanguage { Vietnamese, English }

/// <summary>
/// Nguồn chuỗi đa ngôn ngữ + đổi ngôn ngữ runtime. Bind trong XAML qua indexer:
///   Text="{Binding [key], Source={x:Static loc:Localization.Instance}}"
/// Khi Current đổi, raise PropertyChanged("Item[]") để mọi binding indexer refresh tức thì.
/// </summary>
public sealed class Localization : INotifyPropertyChanged
{
    public static Localization Instance { get; } = new();

    private AppLanguage _current;

    private Localization()
    {
        AppSettings settings = AppSettingsStore.Load();
        _current = settings.Language switch
        {
            "en" => AppLanguage.English,
            "vi" => AppLanguage.Vietnamese,
            // Chưa lưu -> theo culture hệ điều hành (vi -> Tiếng Việt, còn lại -> English).
            _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "vi"
                    ? AppLanguage.Vietnamese : AppLanguage.English
        };
        ToggleCommand = new SimpleCommand(_ => Toggle());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ICommand ToggleCommand { get; }

    public AppLanguage Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            AppSettingsStore.Save(new AppSettings { Language = value == AppLanguage.English ? "en" : "vi" });
            Raise(nameof(Current));
            Raise(nameof(IsEnglish));
            Raise(nameof(IsVietnamese));
            Raise("Item[]"); // refresh mọi binding indexer
        }
    }

    public bool IsEnglish => _current == AppLanguage.English;
    public bool IsVietnamese => _current == AppLanguage.Vietnamese;

    public void Toggle() => Current = IsEnglish ? AppLanguage.Vietnamese : AppLanguage.English;

    public string this[string key]
    {
        get
        {
            var table = _current == AppLanguage.English ? En : Vi;
            return table.TryGetValue(key, out string? value) ? value : key;
        }
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ---- Bảng chuỗi ----
    private static readonly Dictionary<string, string> Vi = new()
    {
        // Header
        ["app.subtitle"] = "HI-FI DSP · AVALONIA",
        ["header.output"] = "OUTPUT",
        ["apo.warning"] = "Equalizer APO hoặc PipeWire EQ chưa sẵn sàng. AutoEQ vẫn phân tích tín hiệu.",

        // Bảng điều khiển
        ["board.control"] = "BẢNG ĐIỀU KHIỂN",
        ["board.control.tag"] = "PRESET",
        ["autoeq.title"] = "Auto EQ",
        ["preset.current"] = "Preset hiện tại",
        ["analysis.source"] = "Trạng thái nguồn",
        ["analysis.confidence"] = "Độ tin cậy",
        ["preset.manual"] = "Preset thủ công",
        ["preset.apply"] = "Áp preset",
        ["listen.options"] = "Tùy chọn nghe",
        ["listen.nearwall"] = "Gần tường",
        ["listen.nearwall.desc"] = "Giảm bass dội khi loa đặt sát tường",
        ["listen.night"] = "Ban đêm",
        ["listen.night.desc"] = "Nén dải động, hạ bass/treble để nghe khuya",
        ["listen.loudness"] = "Bù độ to nhỏ",
        ["listen.loudness.desc"] = "Tăng bass/treble khi mở nhỏ (đường Fletcher–Munson)",
        ["volume.title"] = "Âm lượng",
        ["volume.muted"] = "Đang tắt tiếng",

        // Bảng tín hiệu
        ["board.signal"] = "BẢNG TÍN HIỆU",
        ["board.signal.tag"] = "VINYL · LIVE DSP",
        ["turntable.title"] = "Mâm đĩa tín hiệu",
        ["turntable.desc"] = "Mặt đĩa quay trong mâm cố định; DSP và business logic vẫn nằm trong ViewModel/Services.",
        ["nowplaying.label"] = "Đang phát",
        ["nowplaying.from"] = "Tín hiệu âm thanh đang thu được từ:",
        ["media.prev"] = "Bài trước",
        ["media.playpause"] = "Phát / Tạm dừng",
        ["media.next"] = "Bài kế",

        // Flow card 1 - waveform
        ["flow.wave.title"] = "Tín hiệu vào · Waveform",
        ["flow.wave.desc"] = "Dạng sóng âm thanh thu từ nguồn đang phát",
        ["flow.wave.axis"] = "Trục ngang: thời gian → · Trục dọc: biên độ (độ to)",
        ["flow.wave.legend"] = "đường vàng = đỉnh sóng",
        ["flow.arrow.eq"] = "▼ áp bộ lọc EQ",
        // Flow card 2 - EQ
        ["flow.eq.title"] = "Bộ lọc · Đường EQ",
        ["flow.eq.desc"] = "Mức tăng/giảm theo tần số mà AutoEQ áp vào",
        ["flow.eq.mid"] = "trên vạch giữa = tăng (boost) · dưới = giảm (cut)",
        ["flow.arrow.out"] = "▼ kết quả sau xử lý",
        // Flow card 3 - spectrum
        ["flow.spec.title"] = "Âm ra · Phổ 7 dải",
        ["flow.spec.desc"] = "Năng lượng từng dải tần của âm thanh nghe được",
        ["flow.spec.band"] = "Dải tần",
        ["flow.spec.bar"] = "thanh dài = dải đó càng mạnh",
        ["flow.spec.range"] = "Khoảng",

        // Bảng giám sát
        ["board.monitor"] = "BẢNG GIÁM SÁT",
        ["board.monitor.tag"] = "METERS",
        ["balance.title"] = "Cân bằng âm",
        ["balance.bass"] = "Bass",
        ["balance.vocal"] = "Vocal / Mid",
        ["balance.treble"] = "Treble",
        ["adjust.title"] = "Điều chỉnh AutoEQ",
        ["adjust.hint"] = "boost ↑ · cut ↓",
        ["adjust.bass"] = "Bass",
        ["adjust.vocal"] = "Vocal",
        ["adjust.treble"] = "Treble",
        ["log.title"] = "Nhật ký hoạt động",
        ["device.title"] = "Thiết bị",
        ["device.mainboard"] = "MAINBOARD",
        ["device.soundcard"] = "SOUND CARD",

        // Chuỗi động (VM)
        ["status.analyzing"] = "đang phân tích…",
        ["status.analyzeIn"] = "phân tích sau {0}s",
        ["band.interval"] = "{0} DẢI · {1}S",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        // Header
        ["app.subtitle"] = "HI-FI DSP · AVALONIA",
        ["header.output"] = "OUTPUT",
        ["apo.warning"] = "Equalizer APO or PipeWire EQ is not ready. AutoEQ still analyzes the signal.",

        // Control board
        ["board.control"] = "CONTROL PANEL",
        ["board.control.tag"] = "PRESET",
        ["autoeq.title"] = "Auto EQ",
        ["preset.current"] = "Current preset",
        ["analysis.source"] = "Source state",
        ["analysis.confidence"] = "Confidence",
        ["preset.manual"] = "Manual preset",
        ["preset.apply"] = "Apply preset",
        ["listen.options"] = "Listening options",
        ["listen.nearwall"] = "Near wall",
        ["listen.nearwall.desc"] = "Reduces boomy bass when speakers sit close to a wall",
        ["listen.night"] = "Night mode",
        ["listen.night.desc"] = "Compresses dynamics, lowers bass/treble for late-night listening",
        ["listen.loudness"] = "Loudness compensation",
        ["listen.loudness.desc"] = "Boosts bass/treble at low volume (Fletcher–Munson curve)",
        ["volume.title"] = "Volume",
        ["volume.muted"] = "Muted",

        // Signal board
        ["board.signal"] = "SIGNAL BOARD",
        ["board.signal.tag"] = "VINYL · LIVE DSP",
        ["turntable.title"] = "Signal turntable",
        ["turntable.desc"] = "The record spins inside a fixed platter; DSP and business logic stay in ViewModel/Services.",
        ["nowplaying.label"] = "Now playing",
        ["nowplaying.from"] = "Audio signal captured from:",
        ["media.prev"] = "Previous track",
        ["media.playpause"] = "Play / Pause",
        ["media.next"] = "Next track",

        // Flow card 1 - waveform
        ["flow.wave.title"] = "Input · Waveform",
        ["flow.wave.desc"] = "Sound waveform captured from the playing source",
        ["flow.wave.axis"] = "Horizontal: time → · Vertical: amplitude (loudness)",
        ["flow.wave.legend"] = "gold line = wave peak",
        ["flow.arrow.eq"] = "▼ apply EQ filter",
        // Flow card 2 - EQ
        ["flow.eq.title"] = "Filter · EQ curve",
        ["flow.eq.desc"] = "Per-frequency gain that AutoEQ applies",
        ["flow.eq.mid"] = "above midline = boost · below = cut",
        ["flow.arrow.out"] = "▼ processed result",
        // Flow card 3 - spectrum
        ["flow.spec.title"] = "Output · 7-band spectrum",
        ["flow.spec.desc"] = "Energy of each frequency band you hear",
        ["flow.spec.band"] = "Band",
        ["flow.spec.bar"] = "longer bar = stronger band",
        ["flow.spec.range"] = "Range",

        // Monitor board
        ["board.monitor"] = "MONITOR PANEL",
        ["board.monitor.tag"] = "METERS",
        ["balance.title"] = "Tonal balance",
        ["balance.bass"] = "Bass",
        ["balance.vocal"] = "Vocal / Mid",
        ["balance.treble"] = "Treble",
        ["adjust.title"] = "AutoEQ adjustment",
        ["adjust.hint"] = "boost ↑ · cut ↓",
        ["adjust.bass"] = "Bass",
        ["adjust.vocal"] = "Vocal",
        ["adjust.treble"] = "Treble",
        ["log.title"] = "Activity log",
        ["device.title"] = "Devices",
        ["device.mainboard"] = "MAINBOARD",
        ["device.soundcard"] = "SOUND CARD",

        // Dynamic strings (VM)
        ["status.analyzing"] = "analyzing…",
        ["status.analyzeIn"] = "analyzing in {0}s",
        ["band.interval"] = "{0} BANDS · {1}S",
    };

    private sealed class SimpleCommand : ICommand
    {
        private readonly Action<object?> _execute;
        public SimpleCommand(Action<object?> execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
    }
}

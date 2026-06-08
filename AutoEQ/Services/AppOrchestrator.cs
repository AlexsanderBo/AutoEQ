using System.Globalization;
using System.Text.RegularExpressions;
using AutoEQ.Config;
using AutoEQ.Models;
using AutoEQ.ViewModels;

namespace AutoEQ.Services;

public sealed class AppOrchestrator : IDisposable, IAsyncDisposable
{
    private readonly MainViewModel _vm;
    private readonly IAppLogger _logger = new AppLogger();
    private readonly IDspAnalyzer _analyzer = new DspAnalyzer();
    private readonly IAudioCaptureService _capture;
    private readonly ISystemVolumeService _volume;
    private readonly IAutoEqConfig _config = new AutoEqConfig();
    private readonly IEqualizerApoManager _apo;
    private readonly IPresetApplyService _apply;
    private readonly IPresetEngine _presetEngine;
    private readonly IOutputProfileStore _profiles;
    private readonly INowPlayingService _nowPlaying;
    private readonly INativeAutoEqClient _native;
    private readonly IDefaultDeviceWatcher _deviceWatcher;

    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private PeriodicTimer? _nowPlayingTimer;
    private Task? _nowPlayingLoopTask;
    private OutputAudioProfile? _profile;
    private AudioOutputInfo? _currentOutputInfo;
    private DateTime _lastNativeApplyUtc = DateTime.MinValue;
    private DateTime _lastNativeSnapshotUtc = DateTime.MinValue;
    private DateTime _lastFeatureUtc = DateTime.MinValue;
    private bool _apoOk;
    private int _captureFormatBits;
    private int _captureFormatRate;
    private int _nowPlayingRefreshRunning;


    public AppOrchestrator(MainViewModel viewModel)
    {
        _vm = viewModel;
        _capture = PlatformServiceFactory.CreateAudioCapture(_analyzer);
        _volume = PlatformServiceFactory.CreateSystemVolumeService();
        _apo = PlatformServiceFactory.CreateEqualizerManager(_config, _logger);
        _apply = new PresetApplyService(_apo);
        _presetEngine = new PresetEngine(_logger);
        _profiles = new OutputAudioProfileStore(_logger);
        _nowPlaying = PlatformServiceFactory.CreateNowPlayingService();
        _native = PlatformServiceFactory.CreateNativeAutoEqClient();
        _deviceWatcher = PlatformServiceFactory.CreateDefaultDeviceWatcher();
        WireEvents();
    }

    public async Task StartAsync()
    {
        _vm.SetPresetNames(_presetEngine.GetPresetNames());
        AudioOutputInfo info = _volume.GetAudioOutputInfo();
        _currentOutputInfo = info;
        _vm.Post(() =>
        {
            _vm.OutputDeviceName = info.OutputSummary;
            _vm.MainboardName = info.MainboardName;
            _vm.SoundCardName = $"{info.SoundCardName} {info.SoundCardVersion}".Trim();
        });

        _profile = _profiles.GetOrCreate(info, _vm.NearWallMode, _vm.NightMode);
        string startup = _presetEngine.ChooseStartupPreset(info, _vm.NearWallMode, _vm.NightMode);
        _vm.Post(() => _vm.CurrentPresetName = startup);
        _vm.SetEqCurvePath(BuildCurvePath(_presetEngine.GetPreset(startup)));

        _apoOk = _apo.IsInstalled();
        if (_apoOk)
        {
            await _apo.EnsureIncludeLineAsync().ConfigureAwait(false);
        }
        _vm.Post(() => _vm.IsApoInstalled = _apoOk);
        RefreshAudioFormatText();
        if (!_apoOk) _logger.Error("EQ backend chưa sẵn sàng; AutoEQ vẫn phân tích tín hiệu.");

        _vm.SetVolumeFromSystem(_volume.GetMasterVolumePercent(), _volume.GetMute());
        _vm.SetCaptureStatus(true, "ĐANG KHỞI ĐỘNG");
        _capture.Start();
        _vm.SetCaptureStatus(_capture.IsRunning, _capture.IsRunning ? "LIVE · ỔN ĐỊNH" : "LỖI CAPTURE");

        _deviceWatcher.Start();
        if (_native.IsAvailable()) _native.StartMonitoring(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10));

        _nowPlayingTimer = new PeriodicTimer(TimeSpan.FromMilliseconds(1500));
        _nowPlayingLoopTask = RefreshNowPlayingLoopAsync(_shutdown.Token);
        _logger.Info("AutoEQ đã khởi động orchestration.");
    }

    private async Task RefreshNowPlayingLoopAsync(CancellationToken cancellationToken)
    {
        if (_nowPlayingTimer is null) return;

        try
        {
            while (await _nowPlayingTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshNowPlayingAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void WireEvents()
    {
        _logger.MessageLogged += (_, entry) => _vm.AddSignalLog(entry);
        _capture.FeaturesAvailable += (_, f) => OnFeatures(f);
        _capture.WaveformAvailable += (_, samples) => _vm.UpdateWaveform(samples);
        _capture.FormatChanged += (_, fmt) =>
        {
            _captureFormatRate = fmt.SampleRate;
            _captureFormatBits = fmt.BitsPerSample;
            RefreshAudioFormatText();
            _vm.SetCaptureStatus(true, "LIVE · ỔN ĐỊNH");
        };
        _capture.DeviceChanged += async (_, name) => await OnOutputDeviceChangedAsync(name).ConfigureAwait(false);
        _capture.ErrorOccurred += (_, message) => _logger.Error(message);
        _analyzer.ErrorOccurred += (_, message) => _logger.Error(message);
        _native.ErrorOccurred += (_, message) => _logger.Error(message);
        _native.SnapshotAvailable += async (_, snapshot) => await OnNativeSnapshotAsync(snapshot).ConfigureAwait(false);
        _deviceWatcher.DefaultRenderDeviceChanged += OnDefaultRenderDeviceChanged;

        _volume.VolumeChanged += (_, e) => _vm.SetVolumeFromSystem(e.VolumePercent, e.IsMuted);
        _vm.AutoEqChanged += on => { if (!on) _presetEngine.ResetDecisionWindow(); _logger.Info(on ? "Auto EQ bật." : "Auto EQ tắt; reset cửa sổ quyết định."); };
        _vm.ApplyPresetRequested += async name => await ApplyPresetByNameAsync(name, "manual").ConfigureAwait(false);
        _vm.VoicingOptionsChanged += () => _presetEngine.ResetDecisionWindow();
        _vm.PlayPauseRequested += async () => await _nowPlaying.PlayPauseAsync().ConfigureAwait(false);
        _vm.NextRequested += async () => await _nowPlaying.NextAsync().ConfigureAwait(false);
        _vm.PrevRequested += async () => await _nowPlaying.PreviousAsync().ConfigureAwait(false);
    }

    // Build "48 kHz · 24-bit · APO" từ format thật + trạng thái APO. Trước khi capture chạy
    // thì rate/bits còn 0 -> hiện tạm trạng thái APO.
    private void RefreshAudioFormatText()
    {
        if (_captureFormatRate > 0)
            _vm.SetAudioFormat(_captureFormatRate, _captureFormatBits, _apoOk);
        else
            _vm.Post(() => _vm.AudioFormatText = _apoOk ? "đang đọc... · APO" : "đang đọc... · APO ✗");
    }

    private async void OnFeatures(AudioFeatures f)
    {
        // Đo nhịp phân tích THỰC giữa 2 lần OnFeatures (giây/chu kỳ) -> hiện ở trạng thái tự động.
        DateTime now = DateTime.UtcNow;
        if (_lastFeatureUtc != DateTime.MinValue)
            _vm.SetAnalysisRate((now - _lastFeatureUtc).TotalSeconds);
        _lastFeatureUtc = now;

        _vm.UpdateFeatureFrame(MapState(f.State), f.Confidence, f.Rms, f.CrestFactorDb, new[] { f.SubBass, f.Bass, f.LowMid, f.Mid, f.Presence, f.Treble, f.Air });

        int energy = (int)Math.Clamp(f.Rms * 160, 0, 100);
        int bright = (int)Math.Clamp((f.Treble + f.Air) * 50, 0, 100);
        _vm.UpdateMood(energy, bright, f.Bass * 100, f.Mid * 100, f.Treble * 100);
        // Dynamic AutoEQ liên tục từ DSP managed. Native ưu tiên: nếu snapshot native còn
        // tươi (< NativeFreshnessSeconds) thì để OnNativeSnapshotAsync lo, tránh 2 đường ghi đè nhau.
        if (_profile is null || !_vm.AutoEqEnabled) return;
        if (DateTime.UtcNow - _lastNativeSnapshotUtc < TimeSpan.FromSeconds(AppConfig.NativeFreshnessSeconds)) return;

        PresetEngine.PresetDecision dynamic = _presetEngine.EvaluateDynamicAutoEq(f, _profile, _vm.AutoEqEnabled, _vm.NearWallMode, _vm.NightMode, _vm.LoudnessCompEnabled);
        if (!dynamic.ShouldSwitch || dynamic.RequestedPreset is null) return;
        await ApplyPresetAsync(dynamic.RequestedPreset, dynamic.Reason).ConfigureAwait(false);
    }

    private void OnDefaultRenderDeviceChanged(object? sender, string name)
    {
        // Windows đổi thiết bị phát mặc định: rebind loopback capture sang endpoint mới.
        // Restart() gọi Start() => bắn DeviceChanged => OnOutputDeviceChangedAsync cập nhật
        // UI + nạp lại profile + áp lại preset. Một đường duy nhất, không nhân đôi.
        _logger.Info($"Default output đổi sang: {(string.IsNullOrWhiteSpace(name) ? "(unknown)" : name)}");

        // Phản hồi UI tức thì bằng tên thiết bị mới; OnOutputDeviceChangedAsync sẽ ghi đè bằng
        // summary đầy đủ khi load xong profile.
        if (!string.IsNullOrWhiteSpace(name)) _vm.Post(() => _vm.OutputDeviceName = name);

        // Stop()/Start() là blocking (StopRecording). Đẩy sang background để UI không khựng.
        _ = Task.Run(() =>
        {
            try { _capture.Restart(); }
            catch (Exception ex) { _logger.Error($"Restart capture lỗi: {ex.Message}"); }
        });
    }


    private async Task OnOutputDeviceChangedAsync(string name)

    {
        AudioOutputInfo info = _volume.GetAudioOutputInfo();
        _currentOutputInfo = info;
        _profile = _profiles.GetOrCreate(info, _vm.NearWallMode, _vm.NightMode);
        _vm.Post(() =>
        {
            _vm.OutputDeviceName = string.IsNullOrWhiteSpace(info.OutputSummary) ? name : info.OutputSummary;
            _vm.MainboardName = info.MainboardName;
            _vm.SoundCardName = $"{info.SoundCardName} {info.SoundCardVersion}".Trim();
        });

        // Re-sync volume và re-register callback trên device mới (GetMasterVolumePercent → GetRenderDevice → swap endpoint).
        _vm.SetVolumeFromSystem(_volume.GetMasterVolumePercent(), _volume.GetMute());

        if (!_vm.AutoEqEnabled && !string.IsNullOrWhiteSpace(_vm.CurrentPresetName)
            && _presetEngine.GetPresetNames().Contains(_vm.CurrentPresetName, StringComparer.OrdinalIgnoreCase))
        {
            await ApplyPresetByNameAsync(_vm.CurrentPresetName, "output device changed").ConfigureAwait(false);
        }
        else
        {
            _presetEngine.ResetDecisionWindow();
            string startup = _presetEngine.ChooseStartupPreset(info, _vm.NearWallMode, _vm.NightMode);
            await ApplyPresetByNameAsync(startup, "output device changed").ConfigureAwait(false);
        }
    }

    private async Task OnNativeSnapshotAsync(NativeAutoEqSnapshot snapshot)
    {
        // Đánh dấu native còn "tươi" ngay cả khi snapshot này bị cooldown bỏ qua, để managed
        // dynamic AutoEQ trong OnFeatures biết nhường đường (native đo true-peak chính xác hơn).
        _lastNativeSnapshotUtc = DateTime.UtcNow;
        if (!_vm.AutoEqEnabled || _profile is null) return;
        if (DateTime.UtcNow - _lastNativeApplyUtc < TimeSpan.FromSeconds(AppConfig.PresetCooldownSeconds)) return;
        EqPreset preset = _presetEngine.BuildNativeAutoEqPreset(snapshot, _profile, _vm.NearWallMode, _vm.NightMode, loudnessComp: _vm.LoudnessCompEnabled);

        await ApplyPresetAsync(preset, "native snapshot").ConfigureAwait(false);
        _lastNativeApplyUtc = DateTime.UtcNow;
    }

    private async Task ApplyPresetByNameAsync(string name, string reason) => await ApplyPresetAsync(_presetEngine.GetPreset(name), reason).ConfigureAwait(false);

    private async Task RefreshNowPlayingAsync()
    {
        if (Interlocked.Exchange(ref _nowPlayingRefreshRunning, 1) == 1) return;
        try
        {
            _vm.SetNowPlaying(await _nowPlaying.GetCurrentAsync().ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _logger.Error($"Không đọc được now-playing: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _nowPlayingRefreshRunning, 0);
        }
    }

    private async Task ApplyPresetAsync(EqPreset preset, string reason)
    {
        await _applyGate.WaitAsync(_shutdown.Token).ConfigureAwait(false);
        try
        {
            AutoEqResult result = await _apply.ApplyAsync(preset, _currentOutputInfo, reason, _shutdown.Token).ConfigureAwait(false);
            if (!result.Success) { _logger.Error(result.Message); return; }
            _presetEngine.RememberAppliedCurve(preset);
            _vm.Post(() => _vm.CurrentPresetName = preset.Name);
            (double bassDb, double vocalDb, double trebleDb) = SummarizeBandAdjustments(preset.EqualizerApoText);
            _vm.UpdateAdjustmentSummary(bassDb, vocalDb, trebleDb);

            _vm.SetEqCurvePath(BuildCurvePath(preset));
            _logger.Decision("Preset", $"{preset.Name} · {reason}");
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // App đang thoát, bỏ apply dở để shutdown không treo.
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private static string MapState(string state) => state switch
    {
        "Boomy" => "trầm dày",
        "Harsh Treble" => "treble gắt",
        "Vocal Recessed" => "vocal lùi",
        "Quiet" => "yên tĩnh",
        _ => "cân bằng"
    };

    private static (double BassDb, double VocalDb, double TrebleDb) SummarizeBandAdjustments(string equalizerApoText)
    {
        MatchCollection matches = Regex.Matches(equalizerApoText, @"Fc\s+(?<f>\d+)\s+Hz\s+Gain\s+(?<g>[-+]?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);

        double bassSum = 0, vocalSum = 0, trebleSum = 0;
        int bassCount = 0, vocalCount = 0, trebleCount = 0;

        foreach (Match m in matches)
        {
            double f = double.Parse(m.Groups["f"].Value, CultureInfo.InvariantCulture);
            double g = double.Parse(m.Groups["g"].Value, CultureInfo.InvariantCulture);

            // Bỏ band an toàn sub-bass 32 Hz (luôn là cut cố định, không phải điều chỉnh động).
            if (f <= 40) continue;

            if (f < 250) { bassSum += g; bassCount++; }
            else if (f <= 4000) { vocalSum += g; vocalCount++; }
            else { trebleSum += g; trebleCount++; }
        }

        double Avg(double sum, int count) => count == 0 ? 0 : Math.Round(sum / count, 1);
        return (Avg(bassSum, bassCount), Avg(vocalSum, vocalCount), Avg(trebleSum, trebleCount));
    }

    private static string BuildCurvePath(EqPreset preset)

    {
        MatchCollection matches = Regex.Matches(preset.EqualizerApoText, @"Fc\s+(?<f>\d+)\s+Hz\s+Gain\s+(?<g>[-+]?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (matches.Count == 0) return "M0,55 L340,55";
        var points = matches.Select(m => new
        {
            F = double.Parse(m.Groups["f"].Value, CultureInfo.InvariantCulture),
            G = double.Parse(m.Groups["g"].Value, CultureInfo.InvariantCulture)
        }).OrderBy(p => p.F).Select(p =>
        {
            double x = Math.Clamp((Math.Log10(p.F) - Math.Log10(20)) / (Math.Log10(20000) - Math.Log10(20)) * 340, 0, 340);
            double y = Math.Clamp(55 - (p.G * 5), 8, 102);
            return FormattableString.Invariant($"{x:0.#},{y:0.#}");
        });
        return "M" + string.Join(" L", points);
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _nowPlayingTimer?.Dispose();
        _deviceWatcher.DefaultRenderDeviceChanged -= OnDefaultRenderDeviceChanged;
        _deviceWatcher.Dispose();
        _native.StopMonitoring();
        _capture.Stop();

        _capture.Dispose();
        _apply.Dispose();
        _applyGate.Dispose();
        _shutdown.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (_nowPlayingLoopTask is not null)
        {
            try { await _nowPlayingLoopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        await _native.DisposeAsync().ConfigureAwait(false);
    }
}

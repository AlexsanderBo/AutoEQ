using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AutoEQ.Services;

public interface IDefaultDeviceWatcher : IDisposable
{
    event EventHandler<string>? DefaultRenderDeviceChanged;
    void Start();
}

/// <summary>
/// Lắng nghe Windows đổi default render endpoint (IMMNotificationClient).
/// Windows bắn nhiều callback cho mỗi lần đổi (mỗi Role một lần) nên debounce lại
/// để handler chỉ chạy một lần.
/// </summary>
public sealed class DefaultDeviceWatcher : IDefaultDeviceWatcher, IMMNotificationClient
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly Timer _debounce;
    private readonly object _gate = new();
    private string _lastDeviceId = string.Empty;
    private string _pendingName = string.Empty;
    private bool _registered;

    public event EventHandler<string>? DefaultRenderDeviceChanged;

    public DefaultDeviceWatcher()
    {
        _debounce = new Timer(OnDebounceTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Start()
    {
        if (_registered) return;
        _enumerator.RegisterEndpointNotificationCallback(this);
        _registered = true;

        try
        {
            using MMDevice current = SystemVolumeService.GetWindowsDefaultRenderEndpoint(_enumerator);
            _lastDeviceId = current.ID;
        }
        catch
        {
            // Không có endpoint mặc định lúc khởi động; sẽ cập nhật khi có callback.
        }
    }

    private void OnDebounceTick(object? state)
    {
        _debounce.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        string name;
        lock (_gate)
        {
            name = _pendingName;
        }
        DefaultRenderDeviceChanged?.Invoke(this, name);
    }

    // IMMNotificationClient — chạy trên thread COM/MTA, không phải UI thread.
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        // Chỉ quan tâm thiết bị phát (Render). Role nào cũng được, debounce sẽ gộp lại.
        if (flow != DataFlow.Render) return;
        if (string.IsNullOrWhiteSpace(defaultDeviceId)) return;

        lock (_gate)
        {
            if (string.Equals(defaultDeviceId, _lastDeviceId, StringComparison.OrdinalIgnoreCase)) return;
            _lastDeviceId = defaultDeviceId;
            _pendingName = ResolveFriendlyName(defaultDeviceId);
        }

        _debounce.Change(TimeSpan.FromMilliseconds(300), Timeout.InfiniteTimeSpan);
    }

    private string ResolveFriendlyName(string deviceId)
    {
        try
        {
            using MMDevice device = _enumerator.GetDevice(deviceId);
            return device.FriendlyName;
        }
        catch
        {
            return string.Empty;
        }
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        _debounce.Dispose();
        if (_registered)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(this); }
            catch { /* enumerator có thể đã bị thu hồi */ }
            _registered = false;
        }
        _enumerator.Dispose();
    }
}

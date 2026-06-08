namespace AutoEQ.Services;

internal sealed class LinuxDefaultDeviceWatcher : IDefaultDeviceWatcher
{
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _watchTask;
    private string _lastSink = string.Empty;

    public event EventHandler<string>? DefaultRenderDeviceChanged;

    public void Start()
    {
        if (_watchTask is { IsCompleted: false }) return;
        _watchTask = WatchAsync(_shutdown.Token);
    }

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (!cancellationToken.IsCancellationRequested)
        {
            await CheckOnceAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false)) break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            string sink = await LinuxAudioBackend.GetDefaultSinkAsync(cancellationToken).ConfigureAwait(false);
            if (string.Equals(sink, _lastSink, StringComparison.OrdinalIgnoreCase)) return;

            bool hadPrevious = !string.IsNullOrWhiteSpace(_lastSink);
            _lastSink = sink;
            if (hadPrevious) DefaultRenderDeviceChanged?.Invoke(this, sink);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AutoEQ.Models;

namespace AutoEQ.Services;

public interface INativeAutoEqClient : IAsyncDisposable
{
    event EventHandler<NativeAutoEqSnapshot>? SnapshotAvailable;
    event EventHandler<string>? ErrorOccurred;
    bool IsAvailable();
    void StartMonitoring(TimeSpan interval, TimeSpan timeout);
    void StopMonitoring();
    Task<NativeAutoEqSnapshot?> CaptureSnapshotAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed class NativeWasapiAutoEqClient : INativeAutoEqClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public event EventHandler<NativeAutoEqSnapshot>? SnapshotAvailable;
    public event EventHandler<string>? ErrorOccurred;

    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;

    public bool IsAvailable() => File.Exists(GetExecutablePath());

    public void StartMonitoring(TimeSpan interval, TimeSpan timeout)
    {
        if (_monitorCts is not null) return;
        _monitorCts = new CancellationTokenSource();
        _monitorTask = MonitorLoopAsync(interval, timeout, _monitorCts.Token);
    }

    public void StopMonitoring()
    {
        _monitorCts?.Cancel();
        _monitorCts = null;
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cts = _monitorCts;
        Task? monitorTask = _monitorTask;
        _monitorCts = null;
        _monitorTask = null;

        if (cts is not null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        if (monitorTask is not null)
        {
            try
            {
                await monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private async Task MonitorLoopAsync(TimeSpan interval, TimeSpan timeout, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                NativeAutoEqSnapshot? snapshot = await CaptureSnapshotAsync(timeout, cancellationToken);
                if (snapshot is not null) SnapshotAvailable?.Invoke(this, snapshot);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ErrorOccurred?.Invoke(this, ex.Message);
            }

            try
            {
                await Task.Delay(interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<NativeAutoEqSnapshot?> CaptureSnapshotAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        string exePath = GetExecutablePath();
        if (!File.Exists(exePath)) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--analyze --latency stable --seconds 2",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            }
        };

        process.Start();
        NativeAutoEqSnapshot? latest = null;
        try
        {
            while (!process.StandardOutput.EndOfStream && !cts.IsCancellationRequested)
            {
                string? line = await process.StandardOutput.ReadLineAsync(cts.Token);
                if (string.IsNullOrWhiteSpace(line) || !line.TrimStart().StartsWith('{')) continue;
                latest = JsonSerializer.Deserialize<NativeAutoEqSnapshot>(line, JsonOptions);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            TryKill(process);
        }

        return latest;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between HasExited check and Kill.
        }
        catch (Exception)
        {
            // Best-effort cleanup; never let teardown crash the monitor loop.
        }

    }

    private static string GetExecutablePath() => Path.Combine(AppContext.BaseDirectory, "wasapi_autoeq.exe");
}
namespace AutoEQ.Services;

public sealed record NowPlayingInfo(string Title, string Artist, string Source, bool IsPlaying)
{
    public static NowPlayingInfo Empty { get; } = new("No media session", "", "Media controls unavailable", false);
}

public interface INowPlayingService
{
    Task<NowPlayingInfo> GetCurrentAsync();
    Task<bool> PlayPauseAsync();
    Task<bool> PreviousAsync();
    Task<bool> NextAsync();
    Task<bool> StopAsync();
}

public sealed class NowPlayingService : INowPlayingService
{
    public Task<NowPlayingInfo> GetCurrentAsync() => Task.FromResult(NowPlayingInfo.Empty);

    public Task<bool> PlayPauseAsync() => Task.FromResult(false);

    public Task<bool> PreviousAsync() => Task.FromResult(false);

    public Task<bool> NextAsync() => Task.FromResult(false);

    public Task<bool> StopAsync() => Task.FromResult(false);
}

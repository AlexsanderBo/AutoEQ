using Windows.Media.Control;

namespace WoburnAutoEQ.Services;

public sealed record NowPlayingInfo(string Title, string Artist, string Source, bool IsPlaying)
{
    public static NowPlayingInfo Empty { get; } = new("No media detected", "Open Spotify, Chrome, Edge, or any media app", "Windows Media Session", false);
}

public sealed class NowPlayingService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;

    public async Task<NowPlayingInfo> GetCurrentAsync()
    {
        try
        {
            GlobalSystemMediaTransportControlsSession? session = await GetCurrentSessionAsync();
            if (session == null)
            {
                return NowPlayingInfo.Empty;
            }

            GlobalSystemMediaTransportControlsSessionMediaProperties properties = await session.TryGetMediaPropertiesAsync();
            string title = string.IsNullOrWhiteSpace(properties.Title) ? "Unknown title" : properties.Title;
            string artist = string.IsNullOrWhiteSpace(properties.Artist) ? "Unknown artist" : properties.Artist;
            string source = GetFriendlySourceName(session.SourceAppUserModelId);
            bool isPlaying = session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            return new NowPlayingInfo(title, artist, source, isPlaying);
        }
        catch
        {
            return NowPlayingInfo.Empty;
        }
    }

    public async Task<bool> PlayPauseAsync() => await TryControlAsync(session => session.TryTogglePlayPauseAsync().AsTask());

    public async Task<bool> PreviousAsync() => await TryControlAsync(session => session.TrySkipPreviousAsync().AsTask());

    public async Task<bool> NextAsync() => await TryControlAsync(session => session.TrySkipNextAsync().AsTask());

    public async Task<bool> StopAsync() => await TryControlAsync(session => session.TryStopAsync().AsTask());

    private async Task<GlobalSystemMediaTransportControlsSession?> GetCurrentSessionAsync()
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return _manager.GetCurrentSession();
    }

    private async Task<bool> TryControlAsync(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> action)
    {
        try
        {
            GlobalSystemMediaTransportControlsSession? session = await GetCurrentSessionAsync();
            return session != null && await action(session);
        }
        catch
        {
            return false;
        }
    }

    private static string GetFriendlySourceName(string appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId)) return "Media app";

        string source = appUserModelId.ToLowerInvariant();
        if (source.Contains("spotify")) return "Spotify";
        if (source.Contains("chrome")) return "Google Chrome";
        if (source.Contains("msedge") || source.Contains("edge")) return "Microsoft Edge";
        if (source.Contains("firefox")) return "Firefox";
        if (source.Contains("zune") || source.Contains("media")) return "Windows Media Player";
        if (source.Contains("itunes")) return "Apple Music / iTunes";

        string lastPart = appUserModelId.Split('!', '.', '_').LastOrDefault() ?? appUserModelId;
        return string.IsNullOrWhiteSpace(lastPart) ? appUserModelId : lastPart;
    }
}
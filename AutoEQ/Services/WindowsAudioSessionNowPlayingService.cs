using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AutoEQ.Services;

[SupportedOSPlatform("windows")]
public sealed class WindowsAudioSessionNowPlayingService : INowPlayingService
{
    // Virtual-key code cho phím media toàn cục — app nhạc đang phát sẽ nhận.
    private const byte VkMediaNextTrack = 0xB0;
    private const byte VkMediaPrevTrack = 0xB1;
    private const byte VkMediaPlayPause = 0xB3;
    private const uint KeyeventfKeyUp = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static Task<bool> SendMediaKey(byte vk)
    {
        try
        {
            keybd_event(vk, 0, 0, UIntPtr.Zero);              // key down
            keybd_event(vk, 0, KeyeventfKeyUp, UIntPtr.Zero); // key up
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public Task<NowPlayingInfo> GetCurrentAsync()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using MMDevice device = SystemVolumeService.GetWindowsDefaultRenderEndpoint(enumerator);
            SessionCollection sessions = device.AudioSessionManager.Sessions;

            NowPlayingInfo? active = null;
            NowPlayingInfo? paused = null;
            for (int i = 0; i < sessions.Count; i++)
            {
                AudioSessionControl session = sessions[i];
                AudioSessionState state = session.State;
                // Bỏ qua session đã hết hạn; giữ Active (đang phát) và Inactive (tạm dừng).
                if (state == AudioSessionState.AudioSessionStateExpired) continue;

                int processId = unchecked((int)session.GetProcessID);
                if (processId <= 0 || processId == Environment.ProcessId) continue;

                bool isPlaying = state == AudioSessionState.AudioSessionStateActive;
                NowPlayingInfo? info = TryBuildSessionInfo(session, processId, isPlaying);
                if (info is null) continue;

                if (isPlaying) { active = info; break; }   // ưu tiên cái đang phát
                paused ??= info;                            // nhớ cái tạm dừng làm dự phòng
            }

            return Task.FromResult(active ?? paused ?? NowPlayingInfo.Empty);
        }
        catch
        {
            return Task.FromResult(NowPlayingInfo.Empty);
        }
    }

    public Task<bool> PlayPauseAsync() => SendMediaKey(VkMediaPlayPause);

    public Task<bool> PreviousAsync() => SendMediaKey(VkMediaPrevTrack);

    public Task<bool> NextAsync() => SendMediaKey(VkMediaNextTrack);

    public Task<bool> StopAsync() => SendMediaKey(VkMediaPlayPause);

    private static NowPlayingInfo? TryBuildSessionInfo(AudioSessionControl session, int processId, bool isPlaying)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            string processName = process.ProcessName;
            string source = GetFriendlySourceName(processName);
            (string title, string artist) = GetTitleAndArtist(process, session, source);

            return new NowPlayingInfo(title, artist, source, isPlaying);
        }
        catch
        {
            return null;
        }
    }

    // Trích tên đang xem/nghe từ tiêu đề cửa sổ, bỏ phần đuôi tên app/trình duyệt để không trùng "nguồn".
    private static (string Title, string Artist) GetTitleAndArtist(Process process, AudioSessionControl session, string source)
    {
        string raw = process.MainWindowTitle?.Trim() ?? "";

        // Trình duyệt phát audio bằng process con không có cửa sổ -> tra title ở process anh em cùng tên.
        if (string.IsNullOrWhiteSpace(raw))
            raw = FindSiblingWindowTitle(process.ProcessName);

        if (string.IsNullOrWhiteSpace(raw))
            raw = session.DisplayName?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(raw))
            return ("Đang phát", "");

        // Tiêu đề thường có dạng "Tên bài - Nghệ sĩ - YouTube - Google Chrome".
        // Tách theo " - ", loại các đoạn là tên app/nền tảng để còn lại nội dung thật.
        string[] parts = raw.Split(" - ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var meaningful = new List<string>();
        foreach (string part in parts)
        {
            if (IsAppOrPlatformLabel(part)) continue;
            meaningful.Add(part);
        }

        if (meaningful.Count == 0)
            return (raw, "");

        string title = meaningful[0];
        string artist = meaningful.Count > 1 ? meaningful[1] : "";
        return (title, artist);
    }

    // Duyệt mọi process cùng tên (vd các tiến trình Chrome) lấy MainWindowTitle có ý nghĩa nhất.
    private static string FindSiblingWindowTitle(string processName)
    {
        Process[] siblings = Array.Empty<Process>();
        try
        {
            siblings = Process.GetProcessesByName(processName);
            string best = "";
            foreach (Process sibling in siblings)
            {
                try
                {
                    string title = sibling.MainWindowTitle?.Trim() ?? "";
                    // Ưu tiên cửa sổ có dấu hiệu đang phát media (YouTube/audio thường gắn "▶" hoặc tên nền tảng).
                    if (title.Length > best.Length) best = title;
                }
                catch { /* process có thể vừa thoát */ }
            }
            return best;
        }
        catch
        {
            return "";
        }
        finally
        {
            foreach (Process sibling in siblings)
            {
                try { sibling.Dispose(); } catch { }
            }
        }
    }

    private static bool IsAppOrPlatformLabel(string segment)
    {
        string s = segment.ToLowerInvariant();
        return s is "youtube" or "youtube music" or "spotify" or "soundcloud"
                 or "google chrome" or "microsoft edge" or "firefox" or "cốc cốc" or "coc coc"
                 or "vlc media player" or "windows media player" or "apple music" or "itunes"
                 or "facebook" or "tiktok" or "twitch";
    }

    private static string GetFriendlySourceName(string processName)
    {
        string source = processName.ToLowerInvariant();
        if (source.Contains("spotify")) return "Spotify";
        if (source.Contains("chrome")) return "Google Chrome";
        if (source.Contains("msedge") || source.Contains("edge")) return "Microsoft Edge";
        if (source.Contains("firefox")) return "Firefox";
        if (source.Contains("vlc")) return "VLC";
        if (source.Contains("discord")) return "Discord";
        if (source.Contains("zune") || source.Contains("wmplayer") || source.Contains("media")) return "Windows Media Player";
        if (source.Contains("itunes") || source.Contains("music")) return "Apple Music / iTunes";

        return string.IsNullOrWhiteSpace(processName) ? "Windows audio app" : processName;
    }
}

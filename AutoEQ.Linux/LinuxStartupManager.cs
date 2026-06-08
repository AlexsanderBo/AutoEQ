namespace AutoEQ.Linux;

internal static class LinuxStartupManager
{
    public static bool AutostartExists()
    {
        return File.Exists(GetAutostartPath());
    }

    public static async Task<string> InstallAutostartAsync(CancellationToken cancellationToken = default)
    {
        string desktopPath = GetAutostartPath();
        string autostartDir = Path.GetDirectoryName(desktopPath) ?? throw new InvalidOperationException("Cannot resolve autostart directory.");
        Directory.CreateDirectory(autostartDir);

        string exePath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve current executable path.");
        string content = $"""
        [Desktop Entry]
        Type=Application
        Name=AutoEQ Linux
        Comment=Run AutoEQ Linux background analyzer
        Exec="{exePath}" --background --install-pipewire
        Terminal=false
        X-GNOME-Autostart-enabled=true
        """;

        await File.WriteAllTextAsync(desktopPath, content + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        return desktopPath;
    }

    private static string GetAutostartPath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".config", "autostart", "autoeq-linux.desktop");
    }
}

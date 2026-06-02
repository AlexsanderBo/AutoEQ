namespace AutoEQ.Services;

public interface IAppLogger
{
    event EventHandler<LogEntry>? MessageLogged;
    void Info(string message);
    void Error(string message);
    void Decision(string component, string message);
}

public sealed class AppLogger : IAppLogger
{
    public event EventHandler<LogEntry>? MessageLogged;

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    public void Decision(string component, string message) => Write("INFO", $"[{component}] {message}");

    private void Write(string level, string message)
    {
        MessageLogged?.Invoke(this, new LogEntry(DateTime.Now, level, message));
    }
}

public sealed record LogEntry(DateTime Time, string Level, string Message)
{
    public string Icon => Level == "ERROR" ? "\u26A0" : "\u25CF";

    public string TimeText => Time.ToString("HH:mm:ss");

    public string LevelText => Level == "ERROR" ? "C\u1EA7n ch\u00FA \u00FD" : "Th\u00F4ng tin";

    public string DisplayMessage => Message;
}

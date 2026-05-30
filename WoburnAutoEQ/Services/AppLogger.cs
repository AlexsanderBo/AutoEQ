namespace WoburnAutoEQ.Services;

public sealed class AppLogger
{
    public event EventHandler<string>? MessageLogged;

    public void Info(string message) => Write("INFO", message);

    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        MessageLogged?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {level}: {message}");
    }
}
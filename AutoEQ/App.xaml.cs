using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace AutoEQ;

public partial class App : System.Windows.Application
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "AutoEQ";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterCurrentUserAutoStart();
    }

    private static void RegisterCurrentUserAutoStart()
    {
        try
        {
            string exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return;

            string runCommand = $"\"{exePath}\"";
            using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (runKey?.GetValue(RunValueName) as string == runCommand) return;
            runKey?.SetValue(RunValueName, runCommand, RegistryValueKind.String);
        }
        catch
        {
            // Auto-start is best-effort. App should still run if Windows blocks registry write.
        }
    }
}
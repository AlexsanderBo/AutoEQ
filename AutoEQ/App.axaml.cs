using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System.Diagnostics;
using AutoEQ.Services;
using AutoEQ.ViewModels;
using AutoEQ.Views;

namespace AutoEQ;

public partial class App : Application
{
    private AppOrchestrator? _orchestrator;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            var mainWindow = new MainWindow
            {
                DataContext = viewModel
            };

            if (ShouldStartMinimized(desktop.Args ?? []))
            {
                mainWindow.WindowState = WindowState.Minimized;
            }

            _orchestrator = new AppOrchestrator(viewModel);
            desktop.MainWindow = mainWindow;
            desktop.Startup += async (_, _) => await StartAsync().ConfigureAwait(false);
            desktop.Exit += (_, _) => _orchestrator?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartAsync()
    {
        if (_orchestrator is null) return;

        try
        {
            await _orchestrator.StartAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Không khởi động được AutoEQ: {ex.Message}");
        }
    }

    private static bool ShouldStartMinimized(string[] args)
    {
        return args.Any(arg =>
            string.Equals(arg, "--background", StringComparison.OrdinalIgnoreCase)
            || string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase));
    }
}

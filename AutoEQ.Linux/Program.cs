using AutoEQ.Models;
using AutoEQ.Services;

namespace AutoEQ.Linux;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        LinuxOptions options = LinuxOptions.Parse(args);

        if (options.ShowHelp)
        {
            PrintHelp();
            return 0;
        }

        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("AutoEQ.Linux is meant for Ubuntu/Linux. Use the Avalonia AutoEQ app on Windows.");
            return 2;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        var logger = new AppLogger();
        logger.MessageLogged += (_, entry) => Console.WriteLine($"[{entry.TimeText}] {entry.Level}: {entry.Message}");

        var analyzer = new DspAnalyzer();
        analyzer.ErrorOccurred += (_, message) => Console.Error.WriteLine(message);

        var presetEngine = new PresetEngine(logger);
        var profileStore = new OutputAudioProfileStore(logger);
        var writer = new LinuxPipeWireEqWriter();

        try
        {
            if (options.InstallStartup)
            {
                string desktopPath = await LinuxStartupManager.InstallAutostartAsync(cts.Token).ConfigureAwait(false);
                Console.WriteLine($"Installed Linux autostart: {desktopPath}");
            }

            if (options.InstallPipeWire)
            {
                if (!writer.EqFileExists)
                {
                    await writer.WritePresetAsync(presetEngine.GetPreset(PresetCatalog.Fallback), cts.Token).ConfigureAwait(false);
                }

                await writer.WritePipeWireConfigAsync(cts.Token).ConfigureAwait(false);
                Console.WriteLine($"Wrote PipeWire filter-chain config: {writer.PipeWireConfigPath}");
            }

            if (options.RestartPipeWire)
            {
                await writer.RestartPipeWireAsync(cts.Token).ConfigureAwait(false);
                await writer.TrySetDefaultSinkAsync(cts.Token).ConfigureAwait(false);
                Console.WriteLine("Restarted PipeWire and requested AutoEQ Sink as default output.");
            }

            if (options.SetupOnly)
            {
                return 0;
            }

            string sink = options.SinkName ?? await LinuxAudioCapture.GetDefaultSinkAsync(cts.Token).ConfigureAwait(false);
            AudioOutputInfo outputInfo = LinuxAudioCapture.BuildOutputInfo(sink);
            OutputAudioProfile profile = profileStore.GetOrCreate(outputInfo, nearWallMode: false, nightMode: options.NightMode);

            string startupPresetName = presetEngine.ChooseStartupPreset(outputInfo, nearWallMode: false, nightMode: options.NightMode);
            EqPreset startupPreset = presetEngine.GetPreset(startupPresetName);
            await writer.WritePresetAsync(startupPreset, cts.Token).ConfigureAwait(false);
            presetEngine.RememberAppliedCurve(startupPreset);

            if (!options.NoAutoSetup && !writer.PipeWireConfigExists)
            {
                await writer.WritePipeWireConfigAsync(cts.Token).ConfigureAwait(false);
                Console.WriteLine($"First-run setup wrote PipeWire config: {writer.PipeWireConfigPath}");
                if (!LinuxStartupManager.AutostartExists())
                {
                    string desktopPath = await LinuxStartupManager.InstallAutostartAsync(cts.Token).ConfigureAwait(false);
                    Console.WriteLine($"First-run setup installed autostart: {desktopPath}");
                }

                await TryRestartPipeWireForFirstRunAsync(writer, cts.Token).ConfigureAwait(false);
            }

            Console.WriteLine($"Default sink: {sink}");
            Console.WriteLine($"Initial preset: {startupPreset.Name}");
            Console.WriteLine($"EQ file: {writer.EqFilePath}");
            Console.WriteLine("Capturing output monitor with parec. Press Ctrl+C to stop.");

            var capture = new LinuxAudioCapture(analyzer);
            await foreach (AudioFeatures features in capture.CaptureFeaturesAsync(sink, cts.Token).ConfigureAwait(false))
            {
                PresetEngine.PresetDecision dynamic = presetEngine.EvaluateDynamicAutoEq(
                    features,
                    profile,
                    autoEqEnabled: true,
                    nearWallMode: false,
                    nightMode: options.NightMode,
                    loudnessComp: true);

                if (dynamic.ShouldSwitch && dynamic.RequestedPreset is not null)
                {
                    await writer.WritePresetAsync(dynamic.RequestedPreset, cts.Token).ConfigureAwait(false);
                    presetEngine.RememberAppliedCurve(dynamic.RequestedPreset);
                    Console.WriteLine($"Dynamic EQ updated: {dynamic.RequestedPreset.Name} · {dynamic.Reason}");

                    if (options.RestartOnChange)
                    {
                        await writer.RestartPipeWireAsync(cts.Token).ConfigureAwait(false);
                        await writer.TrySetDefaultSinkAsync(cts.Token).ConfigureAwait(false);
                        Console.WriteLine("PipeWire restarted to apply the updated EQ file.");
                    }
                }
                else if (!options.Quiet)
                {
                    Console.WriteLine($"state={features.State} rms={features.Rms:0.000} centroid={features.SpectralCentroidHz:0}Hz confidence={features.Confidence:0.00}");
                }

                if (options.Once) break;
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task TryRestartPipeWireForFirstRunAsync(LinuxPipeWireEqWriter writer, CancellationToken cancellationToken)
    {
        try
        {
            await writer.RestartPipeWireAsync(cancellationToken).ConfigureAwait(false);
            await writer.TrySetDefaultSinkAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine("First-run setup restarted PipeWire and requested AutoEQ Sink as default output.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"First-run PipeWire restart was skipped/failed: {ex.Message}");
            Console.Error.WriteLine("Open Ubuntu sound settings and select 'AutoEQ Sink' after restarting PipeWire manually.");
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
        AutoEQ.Linux - Ubuntu/PipeWire runner

        Usage:
          autoeq-linux [options]

        Options:
          --install-pipewire     Write ~/.config/pipewire/pipewire.conf.d/99-autoeq-filter-chain.conf
          --restart-pipewire     Restart user PipeWire services and request AutoEQ Sink as default
          --restart-on-change    Restart PipeWire after each dynamic EQ update
          --install-startup      Add ~/.config/autostart/autoeq-linux.desktop
          --setup-only           Run installation steps, then exit before audio capture
          --no-auto-setup        Do not create/restart PipeWire config on first run
          --background           Alias for quiet background-style output
          --night                Use night-mode voicing
          --sink <name>          Capture a specific PulseAudio/PipeWire sink monitor
          --once                 Capture one analysis window, write EQ, then exit
          --quiet                Suppress per-frame status output
          --help                 Show this help

        Ubuntu dependencies:
          sudo apt install pulseaudio-utils pipewire pipewire-pulse
        """);
    }

    private sealed record LinuxOptions(
        bool InstallPipeWire,
        bool RestartPipeWire,
        bool RestartOnChange,
        bool InstallStartup,
        bool SetupOnly,
        bool NoAutoSetup,
        bool NightMode,
        bool Once,
        bool Quiet,
        bool ShowHelp,
        string? SinkName)
    {
        public static LinuxOptions Parse(string[] args)
        {
            bool installPipeWire = false;
            bool restartPipeWire = false;
            bool restartOnChange = false;
            bool installStartup = false;
            bool setupOnly = false;
            bool noAutoSetup = false;
            bool nightMode = false;
            bool once = false;
            bool quiet = false;
            bool showHelp = false;
            string? sinkName = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                switch (arg)
                {
                    case "--install-pipewire":
                        installPipeWire = true;
                        break;
                    case "--restart-pipewire":
                        restartPipeWire = true;
                        break;
                    case "--restart-on-change":
                        restartOnChange = true;
                        break;
                    case "--install-startup":
                        installStartup = true;
                        break;
                    case "--setup-only":
                        setupOnly = true;
                        break;
                    case "--no-auto-setup":
                        noAutoSetup = true;
                        break;
                    case "--background":
                    case "--quiet":
                        quiet = true;
                        break;
                    case "--night":
                        nightMode = true;
                        break;
                    case "--once":
                        once = true;
                        break;
                    case "--help":
                    case "-h":
                        showHelp = true;
                        break;
                    case "--sink":
                        if (i + 1 >= args.Length) throw new ArgumentException("--sink requires a value.");
                        sinkName = args[++i];
                        break;
                    default:
                        throw new ArgumentException($"Unknown option: {arg}");
                }
            }

            return new LinuxOptions(installPipeWire, restartPipeWire, restartOnChange, installStartup, setupOnly, noAutoSetup, nightMode, once, quiet, showHelp, sinkName);
        }
    }
}

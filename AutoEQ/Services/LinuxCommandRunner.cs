using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace AutoEQ.Services;

internal static class LinuxCommandRunner
{
    public static async Task<LinuxCommandResult> RunAsync(string fileName, IEnumerable<string> args, CancellationToken cancellationToken = default)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (string arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) error.AppendLine(e.Data); };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new LinuxCommandResult(127, string.Empty, ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new LinuxCommandResult(process.ExitCode, output.ToString().Trim(), error.ToString().Trim());
    }
}

internal sealed record LinuxCommandResult(int ExitCode, string Output, string Error)
{
    public bool Success => ExitCode == 0;
}

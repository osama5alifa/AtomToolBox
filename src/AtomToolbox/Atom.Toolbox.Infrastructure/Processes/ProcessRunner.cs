using System.Diagnostics;
using System.Text;
using Atom.Toolbox.Application.Abstractions;

namespace Atom.Toolbox.Infrastructure.Processes;

/// <summary>
/// Async process execution with hard timeouts. Never blocks the UI thread and never
/// leaves an orphaned child process behind on cancellation.
/// </summary>
public sealed class ProcessRunner : IProcessRunner
{
    public static ProcessRunner Shared { get; } = new();

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var startInfo = new ProcessStartInfo
        {
            FileName = request.Executable,
            WorkingDirectory = request.WorkingDirectory ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = request.HideWindow,
            WindowStyle = request.HideWindow ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                return new ProcessResult(-1, string.Empty, "The process could not be started.", false, stopwatch.Elapsed);
            }
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, string.Empty, ex.Message, false, stopwatch.Elapsed);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        var timeout = request.Timeout ?? TimeSpan.FromSeconds(30);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            timedOut = !cancellationToken.IsCancellationRequested;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Already exited between the timeout and the kill.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
        }

        string output;
        string error;
        try
        {
            output = await outputTask.ConfigureAwait(false);
            error = await errorTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            output = string.Empty;
            error = string.Empty;
        }

        return new ProcessResult(
            timedOut ? -1 : process.ExitCode,
            output.TrimEnd(),
            error.TrimEnd(),
            timedOut,
            stopwatch.Elapsed);
    }
}

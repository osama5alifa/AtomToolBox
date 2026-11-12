namespace Atom.Toolbox.Application.Abstractions;

public sealed record ProcessRequest(
    string Executable,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    TimeSpan? Timeout = null,
    bool HideWindow = true);

public sealed record ProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    TimeSpan Duration)
{
    public bool Ok => !TimedOut && ExitCode == 0;
    public string Combined => string.Join(Environment.NewLine,
        new[] { StandardOutput, StandardError }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Shell integration and per-user paths. Keeps Win32 out of the presentation layer.</summary>
public interface IEnvironmentService
{
    bool IsElevated { get; }
    string AppDataDirectory { get; }
    string LocalAppDataDirectory { get; }
    string DownloadsDirectory { get; }
    string EngineDirectory { get; }
    string EngineScriptsDirectory { get; }

    void OpenUrl(string url);
    void OpenShortcut(string command);
    void OpenPath(string path);
    void RestartExplorer();

    /// <summary>Extracts the bundled legacy helper scripts on first use and returns their root.</summary>
    string EnsureScriptsWorkspace();
}

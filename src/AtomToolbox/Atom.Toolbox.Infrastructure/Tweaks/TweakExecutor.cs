using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Tweaks;
using Atom.Toolbox.Infrastructure.Registry;
using Atom.Toolbox.Infrastructure.SystemInfo;

namespace Atom.Toolbox.Infrastructure.Tweaks;

/// <summary>
/// Executes the typed catalog. Registry, service and power operations are native;
/// PowerShell and batch remain available only for the handful of legacy scripts that
/// cannot be expressed safely as data.
/// </summary>
public sealed partial class TweakExecutor(
    RegistryStore registry,
    ServiceHost services,
    IProcessRunner processes,
    IEnvironmentService environment,
    IPowerPlanService powerPlans,
    AccentService accents) : ITweakExecutor
{
    public async Task<OperationResult> ApplyAsync(
        TweakDefinition tweak,
        bool enable,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var actions = enable ? tweak.Apply : tweak.Revert;
        if (actions.Count == 0)
        {
            log?.Report($"{tweak.Name}: nothing to change.");
            return OperationResult.Success;
        }

        if (!environment.IsElevated && actions.Any(RequiresElevation))
        {
            return OperationResult.Fail("Administrator rights are required for this change. Restart AtomToolBox as administrator.");
        }

        var errors = new List<string>();
        foreach (var action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            log?.Report("> " + Describe(action));
            try
            {
                var result = await ExecuteAsync(action, log, cancellationToken).ConfigureAwait(false);
                if (!result.Ok)
                {
                    errors.Add(result.Error ?? "Unknown error.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }
        }

        return errors.Count == 0
            ? OperationResult.Success
            : OperationResult.Fail(string.Join(" | ", errors.Distinct(StringComparer.OrdinalIgnoreCase)));
    }

    public async Task<TweakCheckResult> VerifyAsync(TweakDefinition tweak, CancellationToken cancellationToken = default)
    {
        if (tweak.SupportsNativeVerification)
        {
            foreach (var action in tweak.Apply)
            {
                if (action.Kind == TweakActionKinds.Registry)
                {
                    if (!registry.TryRead(action.Hive!, action.Path!, action.Name!, out var actual) ||
                        !Matches(actual, action))
                    {
                        return TweakCheckResult.Off;
                    }
                }
                else if (action.Kind == TweakActionKinds.RegistryDelete)
                {
                    var exists = string.IsNullOrEmpty(action.Name)
                        ? registry.KeyExists(action.Hive!, action.Path!)
                        : registry.ValueExists(action.Hive!, action.Path!, action.Name);
                    if (exists)
                    {
                        return TweakCheckResult.Off;
                    }
                }
            }
            return TweakCheckResult.On;
        }

        return string.IsNullOrWhiteSpace(tweak.LegacyCheck)
            ? TweakCheckResult.Failed("This tweak has no verification command.")
            : await RunLegacyCheckAsync(tweak.LegacyCheck, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OperationResult> ExecuteAsync(
        TweakAction action,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        switch (action.Kind)
        {
            case TweakActionKinds.Registry:
                registry.Set(action.Hive!, action.Path!, action.Name!, action.Type ?? "string", action.Value ?? "");
                return OperationResult.Success;

            case TweakActionKinds.RegistryDelete:
                if (string.IsNullOrEmpty(action.Name))
                {
                    registry.DeleteKeyTree(action.Hive!, action.Path!);
                }
                else
                {
                    registry.DeleteValue(action.Hive!, action.Path!, action.Name);
                }
                return OperationResult.Success;

            case TweakActionKinds.Service:
                if (!string.IsNullOrWhiteSpace(action.Startup))
                {
                    services.SetStartup(action.Service!, action.Startup);
                }
                else if (string.Equals(action.Control, "stop", StringComparison.OrdinalIgnoreCase))
                {
                    services.Stop(action.Service!);
                }
                else if (string.Equals(action.Control, "start", StringComparison.OrdinalIgnoreCase))
                {
                    services.Start(action.Service!);
                }
                return OperationResult.Success;

            case TweakActionKinds.PowerCfg:
                var power = await processes.RunAsync(new ProcessRequest(
                    "powercfg.exe",
                    action.Arguments ?? Array.Empty<string>(),
                    Timeout: TimeSpan.FromSeconds(45)), cancellationToken).ConfigureAwait(false);
                return power.Ok
                    ? OperationResult.Success
                    : OperationResult.Fail(FirstLine(power) ?? "powercfg rejected the change.");

            case TweakActionKinds.PowerScheme:
                var template = action.Arguments?.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(template))
                {
                    return OperationResult.Fail("The power scheme template is missing.");
                }
                var ensured = await powerPlans.EnsureSchemeAsync(template, "Ultimate Performance", cancellationToken).ConfigureAwait(false);
                return ensured.Ok ? OperationResult.Success : OperationResult.Fail(ensured.Error ?? "The power scheme could not be activated.");

            case TweakActionKinds.Process:
                var process = await processes.RunAsync(new ProcessRequest(
                    action.Executable!,
                    action.Arguments ?? Array.Empty<string>(),
                    Timeout: TimeSpan.FromSeconds(90)), cancellationToken).ConfigureAwait(false);
                if (!process.Ok)
                {
                    return OperationResult.Fail(FirstLine(process) ?? $"{action.Executable} failed.");
                }
                LogOutput(log, process);
                return OperationResult.Success;

            case TweakActionKinds.PowerShell:
                var script = await processes.RunAsync(new ProcessRequest(
                    "powershell.exe",
                    ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", action.Script!],
                    Timeout: TimeSpan.FromSeconds(180)), cancellationToken).ConfigureAwait(false);
                if (!script.Ok)
                {
                    return OperationResult.Fail(FirstLine(script) ?? "The PowerShell command failed.");
                }
                LogOutput(log, script);
                return OperationResult.Success;

            case TweakActionKinds.Batch:
                return await RunBatchAsync(action.Script!, log, cancellationToken).ConfigureAwait(false);

            case TweakActionKinds.Accent:
                var accent = accents.Apply(action.Color!);
                log?.Report(accent.Ok ? $"> Accent #{action.Color} applied." : $"> Accent failed: {accent.Error}");
                return accent;

            case TweakActionKinds.Shell:
                var start = new ProcessStartInfo(action.Executable!) { UseShellExecute = true };
                foreach (var argument in action.Arguments ?? Array.Empty<string>())
                {
                    start.Arguments += (start.Arguments.Length > 0 ? " " : "") + argument;
                }
                Process.Start(start);
                return OperationResult.Success;

            default:
                return OperationResult.Fail($"Unsupported action '{action.Kind}'.");
        }
    }

    private async Task<OperationResult> RunBatchAsync(
        string script,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var workspace = environment.EnsureScriptsWorkspace();
        var body = script.Replace("%CD%", environment.EngineDirectory, StringComparison.OrdinalIgnoreCase);
        var file = Path.Combine(Path.GetTempPath(), $"atom_{Guid.NewGuid():N}.bat");
        try
        {
            await File.WriteAllTextAsync(file, body, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            var result = await processes.RunAsync(new ProcessRequest(
                "cmd.exe",
                ["/d", "/s", "/c", file],
                WorkingDirectory: environment.EngineDirectory,
                Timeout: TimeSpan.FromSeconds(180)), cancellationToken).ConfigureAwait(false);

            if (!result.Ok)
            {
                return OperationResult.Fail(FirstLine(result) ?? "The legacy script failed.");
            }
            LogOutput(log, result);
            return OperationResult.Success;
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Temp cleanup is best effort.
            }
        }
    }

    private async Task<TweakCheckResult> RunLegacyCheckAsync(string check, CancellationToken cancellationToken)
    {
        ProcessRequest request;
        var match = PowerShellCheck().Match(check.Trim());
        if (match.Success)
        {
            var body = match.Groups["body"].Value.Trim();
            if (body.StartsWith('"') && body.EndsWith('"') && body.Length > 1)
            {
                body = body[1..^1];
            }
            request = new ProcessRequest(
                "powershell.exe",
                ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", body],
                Timeout: TimeSpan.FromSeconds(30));
        }
        else
        {
            request = new ProcessRequest(
                "cmd.exe",
                ["/d", "/s", "/c", check],
                Timeout: TimeSpan.FromSeconds(30));
        }

        var result = await processes.RunAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.TimedOut)
        {
            return TweakCheckResult.Failed("The state check timed out.");
        }
        return result.ExitCode == 0 ? TweakCheckResult.On : TweakCheckResult.Off;
    }

    private static bool Matches(RegistryValue actual, TweakAction action)
    {
        var expected = action.Value ?? string.Empty;
        try
        {
            return action.Type?.ToLowerInvariant() switch
            {
                "dword" or "qword" =>
                    actual.Data is not null &&
                    Convert.ToInt64(actual.Data, CultureInfo.InvariantCulture) ==
                    long.Parse(expected, CultureInfo.InvariantCulture),
                "binary" =>
                    actual.Data is byte[] bytes &&
                    bytes.AsSpan().SequenceEqual(RegistryStore.ParseBinary(expected)),
                "expand" =>
                    string.Equals(actual.AsText(), Environment.ExpandEnvironmentVariables(expected), StringComparison.Ordinal),
                _ => string.Equals(actual.AsText(), expected, StringComparison.Ordinal)
            };
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool RequiresElevation(TweakAction action) => action.Kind switch
    {
        TweakActionKinds.Registry or TweakActionKinds.RegistryDelete =>
            !string.Equals(action.Hive, "HKCU", StringComparison.OrdinalIgnoreCase),
        TweakActionKinds.Accent => false,
        TweakActionKinds.PowerCfg => action.Arguments?.Any(a =>
            a.Contains("set", StringComparison.OrdinalIgnoreCase) ||
            a.Contains("h", StringComparison.OrdinalIgnoreCase)) ?? false,
        _ => true
    };

    private static string Describe(TweakAction action) => action.Kind switch
    {
        TweakActionKinds.Registry => $"{action.Hive}\\{action.Path}\\{action.Name} = {action.Value}",
        TweakActionKinds.RegistryDelete => $"delete {action.Hive}\\{action.Path}" + (string.IsNullOrEmpty(action.Name) ? "" : $"\\{action.Name}"),
        TweakActionKinds.Service => $"service {action.Service}: {action.Startup ?? action.Control}",
        TweakActionKinds.PowerCfg => "powercfg " + string.Join(' ', action.Arguments ?? Array.Empty<string>()),
        TweakActionKinds.PowerScheme => "activate power scheme " + string.Join(' ', action.Arguments ?? Array.Empty<string>()),
        TweakActionKinds.Process => action.Executable + " " + string.Join(' ', action.Arguments ?? Array.Empty<string>()),
        TweakActionKinds.PowerShell => "powershell " + Truncate(action.Script, 90),
        TweakActionKinds.Batch => "legacy script",
        TweakActionKinds.Accent => "accent #" + action.Color,
        TweakActionKinds.Shell => action.Executable ?? "shell",
        _ => action.Kind
    };

    private static string? FirstLine(ProcessResult result)
    {
        var text = result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput;
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0);
        if (line is null && result.TimedOut)
        {
            return "The command timed out.";
        }
        return line is null ? null : Truncate(line, 220);
    }

    private static void LogOutput(IProgress<string>? log, ProcessResult result)
    {
        if (log is null)
        {
            return;
        }

        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(12))
        {
            log.Report("  " + line.Trim());
        }
    }

    private static string Truncate(string? value, int length) =>
        string.IsNullOrEmpty(value) ? "" : value.Length <= length ? value : value[..length] + "...";

    [GeneratedRegex(@"^powershell(?:\.exe)?\s+.*?-Command\s+(?<body>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PowerShellCheck();
}

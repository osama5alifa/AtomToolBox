using System.Runtime.InteropServices;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Maintenance;
using Atom.Toolbox.Infrastructure.Registry;

namespace Atom.Toolbox.Infrastructure.Maintenance;

/// <summary>
/// Native cleaner. Deleting files directly is considerably faster and quieter than
/// spawning PowerShell per category, and every locked file is skipped instead of aborting
/// the whole category.
/// </summary>
public sealed class CleanerService(
    IProcessRunner processes,
    IEnvironmentService environment,
    ServiceHost services) : ICleanerService
{
    public Task<OperationResult> RunAsync(
        CleanerTask task,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Run(task, log, cancellationToken), cancellationToken);

    private async Task<OperationResult> Run(CleanerTask task, IProgress<string>? log, CancellationToken cancellationToken)
    {
        try
        {
            switch (task.Id)
            {
                case "win_temp":
                {
                    var user = DeleteContents(Path.GetTempPath(), recursive: true);
                    var windowsTemp = DeleteContents(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), true);
                    Report(log, task, (user.Files + windowsTemp.Files, user.Bytes + windowsTemp.Bytes));
                    return OperationResult.Success;
                }

                case "prefetch":
                {
                    var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    Report(log, task, DeleteContents(Path.Combine(windows, "Prefetch"), true));
                    return OperationResult.Success;
                }

                case "recent":
                {
                    var recent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        @"Microsoft\Windows\Recent");
                    Report(log, task, DeleteContents(recent, true));
                    return OperationResult.Success;
                }

                case "recycle_bin":
                {
                    var result = SHEmptyRecycleBin(IntPtr.Zero, null, 0x00000001 | 0x00000002 | 0x00000004);
                    if (result < 0)
                    {
                        return OperationResult.Fail($"Windows returned 0x{result:X8} while emptying the Recycle Bin.");
                    }
                    log?.Report($"{task.Name}: Recycle Bin emptied.");
                    return OperationResult.Success;
                }

                case "event_logs":
                {
                    var cleared = 0;
                    foreach (var name in new[] { "Application", "System", "Security", "Setup", "ForwardedEvents" })
                    {
                        var result = await processes.RunAsync(new ProcessRequest(
                            "wevtutil.exe", ["cl", name], Timeout: TimeSpan.FromSeconds(60)), cancellationToken)
                            .ConfigureAwait(false);
                        if (result.Ok)
                        {
                            cleared++;
                        }
                    }
                    log?.Report($"{task.Name}: {cleared} log(s) cleared.");
                    return OperationResult.Success;
                }

                case "dns_cache":
                {
                    var result = await processes.RunAsync(new ProcessRequest(
                        "ipconfig.exe", ["/flushdns"], Timeout: TimeSpan.FromSeconds(30)), cancellationToken)
                        .ConfigureAwait(false);
                    log?.Report(result.Ok ? $"{task.Name}: DNS cache flushed." : $"{task.Name}: {result.StandardError}");
                    return result.Ok ? OperationResult.Success : OperationResult.Fail("The DNS cache could not be flushed.");
                }

                case "wu_cache":
                {
                    try
                    {
                        services.Stop("wuauserv");
                    }
                    catch (Exception)
                    {
                        // A disabled or missing service is not a reason to skip the cleanup.
                    }

                    var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    var freed = DeleteContents(Path.Combine(windows, @"SoftwareDistribution\Download"), true);
                    try
                    {
                        services.Start("wuauserv");
                    }
                    catch (Exception)
                    {
                        // Windows starts the service again on demand.
                    }
                    Report(log, task, freed);
                    return OperationResult.Success;
                }

                case "thumb_cache":
                {
                    var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        @"Microsoft\Windows\Explorer");
                    var freed = DeleteContents(explorer, false, "thumbcache_*");
                    environment.RestartExplorer();
                    Report(log, task, freed);
                    return OperationResult.Success;
                }

                case "crash_dumps":
                {
                    var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    var minidumps = DeleteContents(Path.Combine(windows, "Minidump"), true);
                    var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
                    var crashDumps = DeleteContents(local, true);
                    Report(log, task, (minidumps.Files + crashDumps.Files, minidumps.Bytes + crashDumps.Bytes));
                    return OperationResult.Success;
                }

                default:
                    return OperationResult.Fail("Unknown cleaner category.");
            }
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail("Cancelled.");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    private static (int Files, long Bytes) DeleteContents(string path, bool recursive, string? pattern = null)
    {
        var files = 0;
        long bytes = 0;
        if (!Directory.Exists(path))
        {
            return (files, bytes);
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(path, pattern ?? "*", recursive
                         ? SearchOption.AllDirectories
                         : SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    var size = info.Length;
                    info.IsReadOnly = false;
                    info.Delete();
                    files++;
                    bytes += size;
                }
                catch (Exception)
                {
                    // In use / access denied: skip and continue.
                }
            }

            if (recursive)
            {
                foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                             .OrderByDescending(d => d.Length))
                {
                    try
                    {
                        Directory.Delete(directory, recursive: false);
                    }
                    catch (Exception)
                    {
                        // Not empty or in use.
                    }
                }
            }
        }
        catch (Exception)
        {
            // The root itself is inaccessible.
        }

        return (files, bytes);
    }

    private static void Report(IProgress<string>? log, CleanerTask task, (int Files, long Bytes) result) =>
        log?.Report($"{task.Name}: removed {result.Files} file(s), freed {FormatBytes(result.Bytes)}.");

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / 1024d / 1024 / 1024:0.00} GB"
        : bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024:0.0} MB"
            : $"{bytes / 1024d:0.0} KB";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);
}

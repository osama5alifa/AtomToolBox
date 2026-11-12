using System.Text;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Tweaks;

namespace Atom.Toolbox.Infrastructure.Maintenance;

/// <summary>Writes a complete, human-readable state report for every catalog tweak.</summary>
public sealed class AuditService(
    ITweakCatalog catalog,
    ITweakExecutor executor,
    IEnvironmentService environment) : IAuditService
{
    public async Task<OperationResult<string>> ExportAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var lines = new List<string>
        {
            "ATOM TOOLBOX — REGISTRY AUDIT REPORT",
            $"Generated : {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}",
            $"Version   : 0.2.5",
            $"Machine   : {Environment.MachineName}",
            $"OS        : {Environment.OSVersion.VersionString}",
            new string('=', 78),
            ""
        };

        var enabled = 0;
        var errors = 0;
        var done = 0;

        foreach (var page in catalog.Pages)
        {
            lines.Add($"## {page.Title}  [{page.Tweaks.Count} TWEAKS]");
            foreach (var tweak in page.Tweaks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await executor.VerifyAsync(tweak, cancellationToken).ConfigureAwait(false);
                var state = result.Error is not null ? "ERR " : result.Enabled ? "ON  " : "OFF ";
                if (result.Enabled) enabled++;
                if (result.Error is not null) errors++;
                lines.Add($"[{state}] {tweak.Name}");
                lines.Add($"        id={tweak.Id}  page={page.Id}");
                if (result.Error is not null)
                {
                    lines.Add($"        check error: {result.Error}");
                }
                progress?.Report(++done);
            }
            lines.Add("");
        }

        lines.Add(new string('=', 78));
        lines.Add($"TOTAL {catalog.TotalTweakCount}  |  ENABLED {enabled}  |  CHECK ERRORS {errors}");
        lines.Add("Values marked ON were verified by native registry read-back or the legacy check command.");

        try
        {
            var path = Path.Combine(environment.DownloadsDirectory,
                $"AtomToolBox-Audit-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
            await File.WriteAllLinesAsync(path, lines, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            return OperationResult<string>.Success(path);
        }
        catch (Exception ex)
        {
            return OperationResult<string>.Fail(ex.Message);
        }
    }
}

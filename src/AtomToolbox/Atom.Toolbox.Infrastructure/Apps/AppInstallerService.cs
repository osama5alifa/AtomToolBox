using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Domain.Maintenance;
using Atom.Toolbox.Infrastructure.Processes;

namespace Atom.Toolbox.Infrastructure.Apps;

/// <summary>
/// Catalog-driven installer: downloads with a pooled HttpClient (no curl process) and hands
/// the file to the vendor's installer. Every operation is cancellable and progress-reporting.
/// </summary>
public sealed class AppInstallerService(IEnvironmentService environment) : IAppInstallerService
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10
    })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.OrdinalIgnoreCase);

    static AppInstallerService()
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("AtomToolBox/0.2.5");
    }

    public async Task<OperationResult> InstallAsync(
        AppCatalogEntry app,
        IProgress<AppInstallProgress> progress,
        CancellationToken cancellationToken = default)
    {
        if (!_running.TryAdd(app.Id, CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)))
        {
            return OperationResult.Fail("This app is already installing.");
        }

        var source = _running[app.Id];
        var token = source.Token;
        var temp = Path.Combine(Path.GetTempPath(), $"atom_{app.Id}_{Guid.NewGuid():N}_{app.FileName}");

        try
        {
            progress.Report(new AppInstallProgress(app.Id, "DOWNLOADING", 0));
            await DownloadAsync(app.Url, temp, app.Id, progress, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            progress.Report(new AppInstallProgress(app.Id, "INSTALLING", 100));
            var install = await InstallPackageAsync(app, temp, token).ConfigureAwait(false);
            progress.Report(new AppInstallProgress(app.Id, install.Ok ? "DONE" : "FAILED", 100, install.Error));
            return install;
        }
        catch (OperationCanceledException)
        {
            progress.Report(new AppInstallProgress(app.Id, "CANCELLED", 0));
            return OperationResult.Fail("Installation cancelled.");
        }
        catch (Exception ex)
        {
            progress.Report(new AppInstallProgress(app.Id, "FAILED", 0, ex.Message));
            return OperationResult.Fail(ex.Message);
        }
        finally
        {
            _running.TryRemove(app.Id, out _);
            source.Dispose();
            try
            {
                File.Delete(temp);
            }
            catch (IOException)
            {
                // Temp cleanup is best effort.
            }
        }
    }

    public void Cancel(string appId)
    {
        if (_running.TryGetValue(appId, out var source))
        {
            source.Cancel();
        }
    }

    private static async Task DownloadAsync(
        string url,
        string destination,
        string appId,
        IProgress<AppInstallProgress> progress,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("The download URL is not valid.");
        }

        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var length = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

        var buffer = new byte[128 * 1024];
        long received = 0;
        var lastReported = -1;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            if (length is > 0)
            {
                var percent = (int)Math.Clamp(received * 100 / length.Value, 0, 100);
                if (percent != lastReported)
                {
                    lastReported = percent;
                    progress.Report(new AppInstallProgress(appId, "DOWNLOADING", percent));
                }
            }
        }

        if (length is > 0 && received != length)
        {
            throw new IOException("The download was interrupted before completion.");
        }
    }

    private async Task<OperationResult> InstallPackageAsync(AppCatalogEntry app, string file, CancellationToken cancellationToken)
    {
        switch (app.InstallType.ToLowerInvariant())
        {
            case "msi":
            {
                var result = await ProcessRunner.Shared.RunAsync(new ProcessRequest(
                    "msiexec.exe",
                    ["/i", file, "/norestart"],
                    Timeout: TimeSpan.FromMinutes(30)), cancellationToken).ConfigureAwait(false);
                return result.Ok
                    ? OperationResult.Success
                    : OperationResult.Fail(Describe(result));
            }

            case "portable":
            {
                var directory = Path.Combine(environment.LocalAppDataDirectory, "Programs", app.Id);
                Directory.CreateDirectory(directory);
                File.Copy(file, Path.Combine(directory, app.FileName), overwrite: true);
                return OperationResult.Success;
            }

            case "zip_extract":
            {
                var directory = Path.Combine(environment.LocalAppDataDirectory, "Programs", app.Id);
                Directory.CreateDirectory(directory);
                ZipFile.ExtractToDirectory(file, directory, overwriteFiles: true);
                return OperationResult.Success;
            }

            default:
                return await RunInteractiveInstallerAsync(file, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<OperationResult> RunInteractiveInstallerAsync(string file, CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo(file)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(file) ?? Path.GetTempPath()
        });

        if (process is null)
        {
            return OperationResult.Fail("The installer could not be started.");
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail(cancellationToken.IsCancellationRequested
                ? "Installation cancelled."
                : "The installer did not finish in time.");
        }

        return process.ExitCode is 0 or 3010
            ? OperationResult.Success
            : OperationResult.Fail($"The installer exited with code {process.ExitCode}.");
    }

    private static string Describe(ProcessResult result)
    {
        var text = result.StandardError.Length > 0 ? result.StandardError : result.StandardOutput;
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return line ?? $"The installer exited with code {result.ExitCode}.";
    }
}

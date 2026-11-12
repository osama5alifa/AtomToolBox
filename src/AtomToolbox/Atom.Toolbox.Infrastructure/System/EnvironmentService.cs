using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Atom.Toolbox.Application.Abstractions;

namespace Atom.Toolbox.Infrastructure.SystemInfo;

public sealed class EnvironmentService : IEnvironmentService
{
    private const string ScriptResourcePrefix = "scripts/";
    private static readonly Lazy<bool> Elevated = new(CheckElevated);
    private static readonly Lazy<string> Downloads = new(ResolveDownloads);

    /// <summary>Portable/test mode: redirects every per-user file under one root.</summary>
    private static string? RootOverride
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("ATOM_DATA_ROOT");
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public bool IsElevated => Elevated.Value;

    public string AppDataDirectory => EnsureDirectory(Path.Combine(
        RootOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtomToolBox"));

    public string LocalAppDataDirectory => EnsureDirectory(Path.Combine(
        RootOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtomToolBox"));

    public string DownloadsDirectory => Downloads.Value;

    public string EngineDirectory => EnsureDirectory(Path.Combine(LocalAppDataDirectory, "engine"));

    // Batches reference helper scripts through %CD%\src\scripts; mirror that layout so a
    // single substitution of %CD% keeps every legacy fallback working untouched.
    public string EngineScriptsDirectory => Path.Combine(EngineDirectory, "src", "scripts");

    /// <summary>
    /// Materialises the bundled helper scripts on first use. Only legacy batch fallbacks
    /// need them, so a normal session never touches the disk for this.
    /// </summary>
    public string EnsureScriptsWorkspace()
    {
        var root = EngineScriptsDirectory;
        var marker = Path.Combine(root, ".version");
        var stamp = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0";
        try
        {
            if (File.Exists(marker) && File.ReadAllText(marker).Trim() == stamp)
            {
                return root;
            }
        }
        catch (IOException)
        {
            return root;
        }

        var assembly = Assembly.GetExecutingAssembly();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ScriptResourcePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = resource[ScriptResourcePrefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var file = File.Create(destination);
            stream.CopyTo(file);
        }

        File.WriteAllText(marker, stamp);
        return root;
    }

    public void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Only http(s) links can be opened.", nameof(url));
        }
        StartShell(uri.AbsoluteUri);
    }

    public void OpenShortcut(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }
        StartShell(command);
    }

    public void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }
        StartShell(path);
    }

    public void RestartExplorer()
    {
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            try
            {
                process.Kill();
            }
            catch (Exception)
            {
                // Explorer may already be exiting.
            }
            finally
            {
                process.Dispose();
            }
        }

        Thread.Sleep(500);
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // Windows restarts the shell on its own in most cases.
        }
    }

    private static void StartShell(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }

    private static bool CheckElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string ResolveDownloads()
    {
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        try
        {
            var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
            var result = SHGetKnownFolderPath(id, 0, IntPtr.Zero, out var pointer);
            if (result != 0 || pointer == IntPtr.Zero)
            {
                return fallback;
            }
            try
            {
                return Marshal.PtrToStringUni(pointer) ?? fallback;
            }
            finally
            {
                Marshal.FreeCoTaskMem(pointer);
            }
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid,
        uint flags,
        IntPtr token,
        out IntPtr path);
}

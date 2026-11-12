using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;

namespace Atom.Toolbox.Infrastructure.Security;

/// <summary>
/// Startup integrity checks: the embedded catalog must hash to the value produced at build
/// time, and a managed/native debugger is treated as an untrusted environment. These are
/// deterrents, not a substitute for code signing and an obfuscated release build.
/// </summary>
public sealed class IntegrityService : IIntegrityService
{
    private readonly Lazy<OperationResult> _result;

    public IntegrityService()
    {
        _result = new Lazy<OperationResult>(Verify, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string Summary
    {
        get
        {
            var result = _result.Value;
            return result.Ok ? "SYSTEM_SECURE" : "INTEGRITY_WARN";
        }
    }

    public Task<OperationResult> VerifyAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_result.Value);

    private static OperationResult Verify()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var names = assembly.GetManifestResourceNames();
            var catalogName = names.FirstOrDefault(n => n.EndsWith(".Catalog.tweaks.json", StringComparison.OrdinalIgnoreCase));
            var hashName = names.FirstOrDefault(n => n.EndsWith(".Catalog.catalog.sha256", StringComparison.OrdinalIgnoreCase));
            if (catalogName is null || hashName is null)
            {
                return OperationResult.Fail("The embedded catalog is missing.");
            }

            byte[] catalogBytes;
            using (var stream = assembly.GetManifestResourceStream(catalogName)!)
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                catalogBytes = memory.ToArray();
            }

            string expected;
            using (var stream = assembly.GetManifestResourceStream(hashName)!)
            using (var reader = new StreamReader(stream))
            {
                expected = reader.ReadToEnd().Trim();
            }

            var actual = Convert.ToHexString(SHA256.HashData(catalogBytes));
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Fail("The tweak catalog failed its integrity check.");
            }

            if (Debugger.IsAttached || IsDebuggerPresent())
            {
                return OperationResult.Fail("A debugger is attached.");
            }

            return OperationResult.Success;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsDebuggerPresent();
}

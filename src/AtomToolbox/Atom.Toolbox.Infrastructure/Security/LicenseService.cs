using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;

namespace Atom.Toolbox.Infrastructure.Security;

/// <summary>
/// Offline RSA licence verification. A token is "payloadB64Url.signatureB64Url", signed with
/// the vendor's private key over the payload bytes. The public key is embedded, so a patched
/// token cannot be made valid without the private key. Entitlement is checked locally; a real
/// store deployment would also reconcile with the purchase backend.
/// </summary>
public sealed class LicenseService : ILicenseService
{
    private readonly string _tokenPath;
    private RSA? _publicKey;
    private bool _isPro;
    private string _licensee = string.Empty;
    private DateTimeOffset? _expires;

    public LicenseService(IEnvironmentService environment)
    {
        _tokenPath = Path.Combine(environment.AppDataDirectory, "license.token");
        LoadPublicKey();
        if (File.Exists(_tokenPath))
        {
            TryApply(File.ReadAllText(_tokenPath).Trim());
        }
    }

    public bool IsPro => _isPro;
    public string TierName => _isPro ? "PRO" : "COMMUNITY";
    public string Licensee => _licensee;
    public DateTimeOffset? Expires => _expires;

    public bool IsUnlocked(string feature) => _isPro;

    public Task<OperationResult> ActivateAsync(string token, CancellationToken cancellationToken = default)
    {
        var result = TryApply(token?.Trim() ?? string.Empty);
        if (!result.Ok)
        {
            return Task.FromResult(result);
        }

        try
        {
            File.WriteAllText(_tokenPath, token!.Trim());
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.Fail($"The licence could not be stored: {ex.Message}"));
        }

        return Task.FromResult(OperationResult.Success);
    }

    private OperationResult TryApply(string token)
    {
        if (_publicKey is null)
        {
            return OperationResult.Fail("Licence verification is not configured in this build.");
        }

        var parts = token.Split('.');
        if (parts.Length != 2)
        {
            return OperationResult.Fail("The licence token is malformed.");
        }

        try
        {
            var payload = Base64UrlDecode(parts[0]);
            var signature = Base64UrlDecode(parts[1]);
            if (!_publicKey.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                return OperationResult.Fail("The licence signature is invalid.");
            }

            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            var tier = root.TryGetProperty("tier", out var tierElement) ? tierElement.GetString() : null;
            if (!string.Equals(tier, "pro", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult.Fail("The licence does not grant the PRO tier.");
            }

            var expiry = root.TryGetProperty("expires", out var expiresElement) &&
                         DateTimeOffset.TryParse(expiresElement.GetString(), out var parsed)
                ? parsed
                : (DateTimeOffset?)null;
            if (expiry is not null && expiry < DateTimeOffset.UtcNow)
            {
                return OperationResult.Fail("The licence has expired.");
            }

            _isPro = true;
            _licensee = root.TryGetProperty("licensee", out var licensee) ? licensee.GetString() ?? "" : "";
            _expires = expiry;
            return OperationResult.Success;
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }

    private void LoadPublicKey()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("license-public.pem", StringComparison.OrdinalIgnoreCase));
            if (name is null)
            {
                return;
            }

            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var rsa = RSA.Create();
            rsa.ImportFromPem(reader.ReadToEnd());
            _publicKey = rsa;
        }
        catch (Exception)
        {
            _publicKey = null;
        }
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var text = value.Replace('-', '+').Replace('_', '/');
        text = text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=');
        return Convert.FromBase64String(text);
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

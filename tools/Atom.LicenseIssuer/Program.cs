using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Development tool: generates the licence key pair and signs offline PRO tokens.
// The private key must never be committed or shipped with the application.

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
var options = ParseOptions(args.Skip(1).ToArray());

switch (command)
{
    case "generate":
    {
        var publicPath = options.GetValueOrDefault("public", "license-public.pem");
        var privatePath = options.GetValueOrDefault("private", "license-private.pem");
        using var rsa = RSA.Create(4096);
        File.WriteAllText(publicPath, rsa.ExportSubjectPublicKeyInfoPem() + Environment.NewLine);
        File.WriteAllText(privatePath, rsa.ExportPkcs8PrivateKeyPem() + Environment.NewLine);
        Console.WriteLine($"Public : {Path.GetFullPath(publicPath)}");
        Console.WriteLine($"Private: {Path.GetFullPath(privatePath)} (keep secret)");
        return 0;
    }

    case "issue":
    {
        var privatePath = options.GetValueOrDefault("private", "license-private.pem");
        var licensee = options.GetValueOrDefault("licensee", "AtomToolBox Licensee");
        var tier = options.GetValueOrDefault("tier", "pro");
        var expires = options.GetValueOrDefault("expires", DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"));
        if (!File.Exists(privatePath))
        {
            Console.Error.WriteLine($"Private key not found: {privatePath}");
            return 2;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            tier,
            licensee,
            expires,
            issued = DateTimeOffset.UtcNow.ToString("O")
        });

        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(privatePath));
        var signature = rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Console.WriteLine(Base64Url(payload) + "." + Base64Url(signature));
        return 0;
    }

    case "verify":
    {
        var publicPath = options.GetValueOrDefault("public", "license-public.pem");
        var token = options.GetValueOrDefault("token", "");
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(publicPath));
        var parts = token.Split('.');
        var valid = parts.Length == 2 &&
                    rsa.VerifyData(Base64UrlDecode(parts[0]), Base64UrlDecode(parts[1]),
                        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Console.WriteLine(valid ? "VALID" : "INVALID");
        if (valid)
        {
            Console.WriteLine(Encoding.UTF8.GetString(Base64UrlDecode(parts[0])));
        }
        return valid ? 0 : 1;
    }

    default:
        Console.WriteLine("""
            AtomToolBox licence issuer
              generate [--public file] [--private file]
              issue    [--private file] [--licensee name] [--tier pro] [--expires yyyy-MM-dd]
              verify   [--public file] [--token token]
            """);
        return 0;
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
        {
            continue;
        }
        result[args[i][2..]] = args[++i];
    }
    return result;
}

static string Base64Url(byte[] value) =>
    Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

static byte[] Base64UrlDecode(string value)
{
    var text = value.Replace('-', '+').Replace('_', '/');
    return Convert.FromBase64String(text.PadRight(text.Length + (4 - text.Length % 4) % 4, '='));
}

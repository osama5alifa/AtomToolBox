using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Atom.CatalogBuilder;

/// <summary>
/// Converts a legacy batch script into typed, idempotent operations. Anything that cannot be
/// represented safely becomes a single embedded batch action, so feature parity is never lost.
/// </summary>
internal static partial class ScriptParser
{
    private static readonly Regex AccentRegex = AccentPattern();
    private static readonly Regex PsCommandRegex = PsCommandPattern();
    private static readonly Regex PsFileRegex = PsFilePattern();

    private static readonly HashSet<string> ShellSafeTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "netsh", "bcdedit", "wevtutil", "ipconfig", "taskkill", "cleanmgr",
        "schtasks", "wmic", "pnputil", "dism", "powercfg"
    };

    private static readonly HashSet<string> FileSystemTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "md", "mkdir", "rd", "rmdir", "del", "erase", "copy", "xcopy", "move",
        "robocopy", "takeown", "icacls", "attrib", "regedit", "forfiles"
    };

    public static ParseOutcome Parse(string batch, string sourceName)
    {
        var actions = new List<TweakAction>();
        var admin = false;

        foreach (var rawLine in batch.Replace("\r\n", "\n").Split('\n'))
        {
            var line = StripRedirection(rawLine.Trim());
            if (line.Length == 0 || IsNoise(line))
            {
                continue;
            }

            if (line.EndsWith('^') || line.StartsWith('(') || line == ")" ||
                line.StartsWith("if ", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("if(", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("if/", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("for ", StringComparison.OrdinalIgnoreCase))
            {
                return Fallback(batch, "control-flow block");
            }

            // "a && b" is executed sequentially by cmd. Split it unless quoted.
            if (line.Contains("&&", StringComparison.Ordinal) && !IsPowerShell(line))
            {
                var parts = SplitOutsideQuotes(line, "&&");
                if (parts.Count > 1)
                {
                    var nested = string.Join("\r\n", parts);
                    var outcome = Parse(nested, sourceName);
                    actions.AddRange(outcome.Actions);
                    admin |= outcome.AdminRequired;
                    continue;
                }
            }

            var tokens = Tokenize(line);
            if (tokens.Count == 0)
            {
                continue;
            }

            var head = tokens[0].ToLowerInvariant();

            if (head is "reg" or "reg.exe")
            {
                var op = ParseReg(tokens);
                if (op is null)
                {
                    return Fallback(batch, $"unsupported reg syntax: {Truncate(line)}");
                }
                actions.Add(op);
                admin |= op.Hive is not "HKCU";
                continue;
            }

            if (head is "sc" or "sc.exe")
            {
                var op = ParseService(tokens);
                if (op is null)
                {
                    return Fallback(batch, $"unsupported sc syntax: {Truncate(line)}");
                }
                actions.Add(op);
                admin = true;
                continue;
            }

            if (head is "powercfg" or "powercfg.exe")
            {
                var args = tokens.Skip(1).ToArray();
                if (args.Length == 0)
                {
                    continue;
                }
                actions.Add(new TweakAction { Kind = "powercfg", Arguments = args });
                admin |= RequiresAdminPowercfg(args);
                continue;
            }

            if (IsPowerShell(line))
            {
                var accent = AccentRegex.Match(line);
                if (accent.Success)
                {
                    actions.Add(new TweakAction { Kind = "accent", Color = accent.Groups["color"].Value.ToUpperInvariant() });
                    admin = false;
                    continue;
                }

                if (PsFileRegex.IsMatch(line))
                {
                    return Fallback(batch, "external PowerShell helper");
                }

                var commandMatch = PsCommandRegex.Match(line);
                if (!commandMatch.Success)
                {
                    return Fallback(batch, "unsupported PowerShell invocation");
                }

                var body = commandMatch.Groups["body"].Value.Trim();
                if (body.StartsWith('"') && body.EndsWith('"') && body.Length > 1)
                {
                    body = body[1..^1];
                }
                if (body.Contains("%CD%", StringComparison.OrdinalIgnoreCase))
                {
                    return Fallback(batch, "PowerShell helper path");
                }

                actions.Add(new TweakAction { Kind = "powershell", Script = body });
                admin |= ContainsMachineWrite(body);
                continue;
            }

            if (head is "start")
            {
                actions.Add(new TweakAction
                {
                    Kind = "shell",
                    Executable = tokens.Count > 1 ? tokens[1] : "cmd.exe",
                    Arguments = tokens.Count > 2 ? tokens.Skip(2).ToArray() : Array.Empty<string>()
                });
                continue;
            }

            if (ShellSafeTools.Contains(head))
            {
                var exe = head.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? head : head + ".exe";
                actions.Add(new TweakAction { Kind = "process", Executable = exe, Arguments = tokens.Skip(1).ToArray() });
                admin |= head is not ("ipconfig" or "cleanmgr");
                continue;
            }

            if (FileSystemTools.Contains(head))
            {
                return Fallback(batch, $"file-system command: {head}");
            }

            if (head is "echo" or "timeout" or "ping" or "cls" or "color")
            {
                continue;
            }

            return Fallback(batch, $"unrecognised command: {head}");
        }

        // A script that only prints or exits is a deliberate no-op (for example the OFF
        // direction of an irreversible removal). That is success, not a parse failure.
        return new ParseOutcome(actions, admin, null);
    }

    private static TweakAction? ParseReg(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 3)
        {
            return null;
        }

        var verb = tokens[1].ToLowerInvariant();
        if (!TrySplitHive(tokens[2], out var hive, out var path))
        {
            return null;
        }

        if (verb == "delete")
        {
            string? name = null;
            for (var i = 3; i < tokens.Count; i++)
            {
                if (tokens[i].Equals("/v", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
                {
                    name = tokens[++i];
                }
                else if (!tokens[i].Equals("/f", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }
            return new TweakAction { Kind = "registry-delete", Hive = hive, Path = path, Name = name };
        }

        if (verb != "add")
        {
            return null;
        }

        string? valueName = null;
        string? type = null;
        string? data = null;

        for (var i = 3; i < tokens.Count; i++)
        {
            var token = tokens[i];
            switch (token.ToLowerInvariant())
            {
                case "/v":
                    if (i + 1 >= tokens.Count) return null;
                    valueName = tokens[++i];
                    break;
                case "/t":
                    if (i + 1 >= tokens.Count) return null;
                    type = NormalizeType(tokens[++i]);
                    if (type is null) return null;
                    break;
                case "/d":
                    if (i + 1 >= tokens.Count) return null;
                    data = tokens[++i];
                    // Unquoted values that contained spaces arrive as several tokens.
                    while (i + 1 < tokens.Count && !tokens[i + 1].StartsWith('/'))
                    {
                        data += " " + tokens[++i];
                    }
                    break;
                case "/ve":
                    valueName = string.Empty;
                    break;
                case "/f":
                case "/reg:64":
                case "/reg:32":
                    break;
                default:
                    return null;
            }
        }

        if (type is null)
        {
            type = "string";
        }

        var normalized = NormalizeData(type, data);
        if (normalized is null)
        {
            return null;
        }

        return new TweakAction
        {
            Kind = "registry",
            Hive = hive,
            Path = path,
            Name = valueName ?? string.Empty,
            Type = type,
            Value = normalized
        };
    }

    private static TweakAction? ParseService(IReadOnlyList<string> tokens)
    {
        if (tokens.Count < 3)
        {
            return null;
        }

        var action = tokens[1].ToLowerInvariant();
        var name = tokens[2];

        if (action == "config")
        {
            string? startup = null;
            for (var i = 3; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Equals("start=", StringComparison.OrdinalIgnoreCase) && i + 1 < tokens.Count)
                {
                    startup = tokens[++i].ToLowerInvariant();
                }
                else if (token.StartsWith("start=", StringComparison.OrdinalIgnoreCase))
                {
                    startup = token[6..].ToLowerInvariant();
                }
                else if (token.Equals("binpath=", StringComparison.OrdinalIgnoreCase) || token.StartsWith("binpath=", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }
            }

            return startup is null
                ? null
                : new TweakAction { Kind = "service", Service = name, Startup = NormalizeStartup(startup) };
        }

        if (action is "stop" or "start")
        {
            return new TweakAction { Kind = "service", Service = name, Control = action };
        }

        return null;
    }

    private static string NormalizeStartup(string value) => value switch
    {
        "disabled" => "disabled",
        "demand" or "manual" => "manual",
        "auto" => "automatic",
        "delayed-auto" or "delayed" => "delayed",
        _ => value
    };

    private static string? NormalizeType(string value) => value.ToUpperInvariant() switch
    {
        "REG_DWORD" => "dword",
        "REG_QWORD" => "qword",
        "REG_SZ" => "string",
        "REG_EXPAND_SZ" => "expand",
        "REG_BINARY" => "binary",
        "REG_MULTI_SZ" => "multistring",
        "REG_NONE" => "none",
        _ => null
    };

    private static string? NormalizeData(string type, string? data)
    {
        data ??= string.Empty;
        switch (type)
        {
            case "dword":
                if (!TryParseNumber(data, out var dword) || dword < 0 || dword > uint.MaxValue)
                {
                    return null;
                }
                return dword.ToString(CultureInfo.InvariantCulture);

            case "qword":
                if (!TryParseNumber(data, out var qword) || qword < 0)
                {
                    return null;
                }
                return qword.ToString(CultureInfo.InvariantCulture);

            case "binary":
                // REG_BINARY accepts comma separated bytes ("01,02,ff") and the contiguous
                // hex form used by the legacy visual-effect scripts ("9e3e038010000000").
                var parts = data.Contains(',')
                    ? data.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : ChunkPairs(data.Replace(" ", string.Empty));
                var builder = new StringBuilder();
                foreach (var part in parts)
                {
                    if (!byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                    {
                        return null;
                    }
                    if (builder.Length > 0) builder.Append(',');
                    builder.Append(b.ToString("X2", CultureInfo.InvariantCulture));
                }
                return builder.ToString();

            default:
                return data;
        }
    }

    private static bool TryParseNumber(string text, out long value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TrySplitHive(string key, out string hive, out string path)
    {
        hive = string.Empty;
        path = string.Empty;
        var slash = key.IndexOf('\\');
        var root = slash < 0 ? key : key[..slash];
        path = slash < 0 ? string.Empty : key[(slash + 1)..];

        hive = root.ToUpperInvariant() switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => "HKCU",
            "HKLM" or "HKEY_LOCAL_MACHINE" => "HKLM",
            "HKCR" or "HKEY_CLASSES_ROOT" => "HKCR",
            "HKU" or "HKEY_USERS" => "HKU",
            "HKCC" or "HKEY_CURRENT_CONFIG" => "HKCC",
            _ => string.Empty
        };

        return hive.Length > 0 && path.Length > 0;
    }

    private static bool RequiresAdminPowercfg(IReadOnlyList<string> args) =>
        args.Any(a =>
        {
            var s = a.TrimStart('/', '-').ToLowerInvariant();
            return s is "setacvalueindex" or "setdcvalueindex" or "setactive" or "change" or "h"
                or "hibernate" or "import" or "duplicatescheme" or "delete" or "attributes"
                or "restoredefaultschemes";
        });

    private static bool ContainsMachineWrite(string script) =>
        script.Contains("HKLM", StringComparison.OrdinalIgnoreCase) ||
        script.Contains("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ||
        script.Contains("HKCR", StringComparison.OrdinalIgnoreCase) ||
        script.Contains("sc.exe", StringComparison.OrdinalIgnoreCase) ||
        script.Contains("powercfg", StringComparison.OrdinalIgnoreCase) ||
        script.Contains("bcdedit", StringComparison.OrdinalIgnoreCase) ||
        script.Contains("Set-CimInstance", StringComparison.OrdinalIgnoreCase);

    private static bool IsNoise(string line) =>
        line.StartsWith("@echo", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("rem ", StringComparison.OrdinalIgnoreCase) ||
        line.Equals("rem", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("::", StringComparison.Ordinal) ||
        line.StartsWith("echo ", StringComparison.OrdinalIgnoreCase) ||
        line.Equals("echo off", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("title ", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("setlocal", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("endlocal", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("exit /b", StringComparison.OrdinalIgnoreCase) ||
        line.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("pushd", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("cd /d", StringComparison.OrdinalIgnoreCase);

    private static ParseOutcome Fallback(string batch, string reason) =>
        new(new TweakAction[] { new() { Kind = "batch", Script = batch } }, true, reason);

    private static bool IsPowerShell(string line) =>
        line.StartsWith("powershell", StringComparison.OrdinalIgnoreCase) ||
        line.StartsWith("\"powershell", StringComparison.OrdinalIgnoreCase);

    private static List<string> SplitOutsideQuotes(string value, string separator)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '"') quoted = !quoted;

            if (!quoted && i + separator.Length <= value.Length &&
                value.AsSpan(i, separator.Length).SequenceEqual(separator))
            {
                parts.Add(current.ToString().Trim());
                current.Clear();
                i += separator.Length - 1;
                continue;
            }
            current.Append(c);
        }

        parts.Add(current.ToString().Trim());
        return parts.Where(p => p.Length > 0).ToList();
    }

    private static List<string> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var tokenStarted = false;

        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
                tokenStarted = true;
                continue;
            }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (tokenStarted)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    tokenStarted = false;
                }
                continue;
            }
            current.Append(c);
            tokenStarted = true;
        }

        if (tokenStarted) tokens.Add(current.ToString());
        return tokens;
    }

    private static string StripRedirection(string line)
    {
        var stripped = TrailingRedirect().Replace(line, string.Empty).Trim();
        return stripped.Length == 0 ? line : stripped;
    }

    private static string[] ChunkPairs(string hex)
    {
        if (hex.Length == 0 || hex.Length % 2 != 0)
        {
            return [hex];
        }

        var result = new string[hex.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = hex.Substring(i * 2, 2);
        }
        return result;
    }

    private static string Truncate(string value) => value.Length <= 70 ? value : value[..70] + "...";

    [GeneratedRegex(@"accent_set\.ps1.*?-Color\s+(?<color>[0-9A-Fa-f]{6})", RegexOptions.IgnoreCase)]
    private static partial Regex AccentPattern();

    [GeneratedRegex(@"-Command\s+(?<body>.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PsCommandPattern();

    [GeneratedRegex(@"-File\s+", RegexOptions.IgnoreCase)]
    private static partial Regex PsFilePattern();

    [GeneratedRegex(@"(?:\s*[12]?>\s*(?:nul|&1)|<nul)+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingRedirect();
}

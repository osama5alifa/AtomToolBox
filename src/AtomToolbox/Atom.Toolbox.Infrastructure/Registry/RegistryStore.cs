using Microsoft.Win32;

namespace Atom.Toolbox.Infrastructure.Registry;

public enum RegistryValueType
{
    None,
    String,
    ExpandString,
    DWord,
    QWord,
    Binary,
    MultiString
}

public sealed record RegistryValue(RegistryValueType Type, object? Data)
{
    public string AsText() => Data switch
    {
        null => string.Empty,
        byte[] bytes => Convert.ToHexString(bytes),
        string[] lines => string.Join('\0', lines),
        _ => Convert.ToString(Data, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
    };
}

/// <summary>
/// Thin, 64-bit-view registry access with consistent error translation. Every tweak
/// write goes through here so access-denied and missing-key cases are handled once.
/// </summary>
public sealed class RegistryStore
{
    public bool TryRead(string hive, string path, string name, out RegistryValue value)
    {
        value = null!;
        using var baseKey = OpenBase(hive);
        using var key = baseKey.OpenSubKey(path, writable: false);
        if (key is null)
        {
            return false;
        }

        var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null)
        {
            return false;
        }

        var kind = key.GetValueKind(name);
        value = new RegistryValue(ToDomain(kind), raw);
        return true;
    }

    public bool ValueExists(string hive, string path, string name)
    {
        using var baseKey = OpenBase(hive);
        using var key = baseKey.OpenSubKey(path, writable: false);
        if (key is null)
        {
            return false;
        }
        return key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase);
    }

    public bool KeyExists(string hive, string path)
    {
        using var baseKey = OpenBase(hive);
        using var key = baseKey.OpenSubKey(path, writable: false);
        return key is not null;
    }

    public void SetDWord(string hive, string path, string name, long value)
    {
        using var key = CreateKey(hive, path);
        key.SetValue(name, unchecked((int)(uint)value), RegistryValueKind.DWord);
    }

    public void SetQWord(string hive, string path, string name, long value)
    {
        using var key = CreateKey(hive, path);
        key.SetValue(name, value, RegistryValueKind.QWord);
    }

    public void SetString(string hive, string path, string name, string value, bool expand)
    {
        using var key = CreateKey(hive, path);
        key.SetValue(name, value, expand ? RegistryValueKind.ExpandString : RegistryValueKind.String);
    }

    public void SetBinary(string hive, string path, string name, byte[] value)
    {
        using var key = CreateKey(hive, path);
        key.SetValue(name, value, RegistryValueKind.Binary);
    }

    public void SetMultiString(string hive, string path, string name, string[] value)
    {
        using var key = CreateKey(hive, path);
        key.SetValue(name, value, RegistryValueKind.MultiString);
    }

    public void DeleteValue(string hive, string path, string name)
    {
        using var baseKey = OpenBase(hive);
        using var key = baseKey.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void DeleteKeyTree(string hive, string path)
    {
        using var baseKey = OpenBase(hive);
        baseKey.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
    }

    public void EnsureKey(string hive, string path)
    {
        using var key = CreateKey(hive, path);
    }

    /// <summary>Creates or overwrites a value from its catalog description.</summary>
    public void Set(string hive, string path, string name, string type, string value)
    {
        switch (type.ToLowerInvariant())
        {
            case "dword":
                SetDWord(hive, path, name, long.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "qword":
                SetQWord(hive, path, name, long.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "binary":
                SetBinary(hive, path, name, ParseBinary(value));
                break;
            case "expand":
                SetString(hive, path, name, Environment.ExpandEnvironmentVariables(value), expand: true);
                break;
            case "multistring":
                SetMultiString(hive, path, name, value.Split('\0'));
                break;
            case "none":
                break;
            default:
                SetString(hive, path, name, Environment.ExpandEnvironmentVariables(value), expand: false);
                break;
        }
    }

    public static byte[] ParseBinary(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => Convert.ToByte(part, 16))
            .ToArray();

    private static RegistryKey OpenBase(string hive) => hive.ToUpperInvariant() switch
    {
        "HKCU" => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64),
        "HKLM" => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
        "HKCR" => RegistryKey.OpenBaseKey(RegistryHive.ClassesRoot, RegistryView.Registry64),
        "HKU" => RegistryKey.OpenBaseKey(RegistryHive.Users, RegistryView.Registry64),
        "HKCC" => RegistryKey.OpenBaseKey(RegistryHive.CurrentConfig, RegistryView.Registry64),
        _ => throw new ArgumentOutOfRangeException(nameof(hive), hive, "Unsupported registry hive.")
    };

    private static RegistryKey CreateKey(string hive, string path)
    {
        var baseKey = OpenBase(hive);
        try
        {
            return baseKey.CreateSubKey(path, writable: true)
                   ?? throw new IOException($"The registry key could not be created: {hive}\\{path}");
        }
        finally
        {
            baseKey.Dispose();
        }
    }

    private static RegistryValueType ToDomain(RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.String => RegistryValueType.String,
        RegistryValueKind.ExpandString => RegistryValueType.ExpandString,
        RegistryValueKind.DWord => RegistryValueType.DWord,
        RegistryValueKind.QWord => RegistryValueType.QWord,
        RegistryValueKind.Binary => RegistryValueType.Binary,
        RegistryValueKind.MultiString => RegistryValueType.MultiString,
        _ => RegistryValueType.None
    };
}

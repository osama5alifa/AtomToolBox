using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atom.Toolbox.Infrastructure.State;

/// <summary>Crash-safe JSON persistence for the small per-user state files.</summary>
internal static class JsonFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static T Read<T>(string path, T fallback)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? fallback
                : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    public static void Write<T>(string path, T value)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception)
        {
            // State persistence must never take the UI down.
        }
    }
}

using System.Text.Json;
using Atom.Toolbox.Application.Abstractions;

namespace Atom.Toolbox.Infrastructure.Processes;

/// <summary>Helpers for the few Windows subsystems whose only practical API is PowerShell (AppX).</summary>
public static class PowerShell
{
    public static ProcessRequest Command(string script, TimeSpan? timeout = null, bool bypass = true)
    {
        var arguments = new List<string> { "-NoProfile", "-NonInteractive" };
        if (bypass)
        {
            arguments.Add("-ExecutionPolicy");
            arguments.Add("Bypass");
        }
        arguments.Add("-Command");
        arguments.Add(script);
        return new ProcessRequest("powershell.exe", arguments, Timeout: timeout ?? TimeSpan.FromMinutes(2));
    }

    public static ProcessRequest File(string scriptPath, IEnumerable<string> arguments, TimeSpan? timeout = null)
    {
        var list = new List<string> { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath };
        list.AddRange(arguments);
        return new ProcessRequest("powershell.exe", list, Timeout: timeout ?? TimeSpan.FromMinutes(2));
    }

    public static JsonDocument? ParseJson(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var text = output.Trim();
        var start = text.IndexOfAny(['{', '[']);
        if (start < 0)
        {
            return null;
        }

        var candidate = text[start..];
        // Trim anything PowerShell printed after the JSON document.
        var end = candidate.LastIndexOfAny(['}', ']']);
        if (end >= 0)
        {
            candidate = candidate[..(end + 1)];
        }

        try
        {
            return JsonDocument.Parse(candidate, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

using System.Globalization;
using System.Runtime.InteropServices;
using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Common;
using Atom.Toolbox.Infrastructure.Registry;

namespace Atom.Toolbox.Infrastructure.SystemInfo;

/// <summary>
/// Applies a Windows accent colour the same way Settings does: DWM + Explorer accent
/// values, the derived Start palette, an ImmersiveColorSet broadcast and a shell restart.
/// Implemented natively so the accent tweaks no longer depend on a PowerShell helper.
/// </summary>
public sealed class AccentService(
    RegistryStore registry,
    IEnvironmentService environment)
{
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";
    private const string AccentKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    private static readonly byte[] StockBluePalette =
    [
        0xA6, 0xD8, 0xFF, 0x00, 0x76, 0xB9, 0xED, 0x00,
        0x42, 0x9C, 0xE3, 0x00, 0x00, 0x78, 0xD7, 0x00,
        0x00, 0x5A, 0x9E, 0x00, 0x00, 0x42, 0x75, 0x00,
        0x00, 0x26, 0x42, 0x00, 0xF7, 0x63, 0x0C, 0x00
    ];

    public OperationResult Apply(string hex)
    {
        if (hex.Length != 6 ||
            !byte.TryParse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(hex[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return OperationResult.Fail("Invalid accent colour.");
        }

        var accent = unchecked((int)(0xFF000000u | (uint)(b << 16) | (uint)(g << 8) | r));
        var colorization = unchecked((int)(0xC4000000u | (uint)(r << 16) | (uint)(g << 8) | b));
        var palette = BuildPalette(r, g, b);

        registry.SetDWord("HKCU", DwmKey, "AccentColor", accent);
        registry.SetDWord("HKCU", DwmKey, "ColorizationColor", colorization);
        registry.SetDWord("HKCU", AccentKey, "AccentColorMenu", accent);
        registry.SetDWord("HKCU", AccentKey, "StartColorMenu", accent);
        registry.SetBinary("HKCU", AccentKey, "AccentPalette", palette);

        if (!Verify("HKCU", DwmKey, "AccentColor", accent) ||
            !Verify("HKCU", DwmKey, "ColorizationColor", colorization) ||
            !Verify("HKCU", AccentKey, "AccentColorMenu", accent) ||
            !Verify("HKCU", AccentKey, "StartColorMenu", accent) ||
            !Verify("HKCU", AccentKey, "AccentPalette", palette))
        {
            return OperationResult.Fail("Windows did not store the accent values (read-back mismatch).");
        }

        BroadcastColorChange();
        environment.RestartExplorer();
        return OperationResult.Success;
    }

    private byte[] BuildPalette(byte r, byte g, byte b)
    {
        if (string.Equals(Hex(r, g, b), "0078D7", StringComparison.OrdinalIgnoreCase))
        {
            return (byte[])StockBluePalette.Clone();
        }

        var palette = new byte[32];
        var lights = new[] { 0.70, 0.48, 0.26 };
        for (var i = 0; i < lights.Length; i++)
        {
            palette[i * 4] = LerpWhite(r, lights[i]);
            palette[i * 4 + 1] = LerpWhite(g, lights[i]);
            palette[i * 4 + 2] = LerpWhite(b, lights[i]);
        }

        palette[12] = r;
        palette[13] = g;
        palette[14] = b;

        var darks = new[] { 0.74, 0.545, 0.31 };
        for (var i = 0; i < darks.Length; i++)
        {
            palette[16 + i * 4] = Darken(r, darks[i]);
            palette[17 + i * 4] = Darken(g, darks[i]);
            palette[18 + i * 4] = Darken(b, darks[i]);
        }

        // Slot eight is machine specific. Windows keeps it, so do the same.
        if (registry.TryRead("HKCU", AccentKey, "AccentPalette", out var existing) &&
            existing.Data is byte[] current && current.Length >= 32)
        {
            Array.Copy(current, 28, palette, 28, 4);
        }

        return palette;
    }

    private bool Verify(string hive, string path, string name, int expected) =>
        registry.TryRead(hive, path, name, out var value) &&
        value.Data is not null &&
        Convert.ToInt64(value.Data, CultureInfo.InvariantCulture) == expected;

    private bool Verify(string hive, string path, string name, byte[] expected) =>
        registry.TryRead(hive, path, name, out var value) &&
        value.Data is byte[] actual &&
        actual.AsSpan().SequenceEqual(expected);

    private static byte LerpWhite(byte value, double amount) =>
        (byte)Math.Clamp(Math.Round(value + (255 - value) * amount), 0, 255);

    private static byte Darken(byte value, double factor) =>
        (byte)Math.Clamp(Math.Round(value * factor), 0, 255);

    private static string Hex(byte r, byte g, byte b) => $"{r:X2}{g:X2}{b:X2}";

    private static void BroadcastColorChange()
    {
        try
        {
            _ = SendMessageTimeout(
                new IntPtr(0xFFFF), 0x001A, IntPtr.Zero, "ImmersiveColorSet",
                0x0002, 2000, out _);
        }
        catch (Exception)
        {
            // The broadcast is best effort; Explorer's restart below re-reads the values.
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint message, IntPtr wParam, string lParam,
        uint flags, uint timeout, out IntPtr result);
}

using System.Runtime.InteropServices;

// Copies icon groups and version information from the application executable into the
// 7-Zip SFX stub before an installer is built. This gives AtomToolbox-Setup.exe the correct
// icon and file properties without a resource editor or a paid installer toolchain.

var options = Parse(args);
var source = options.GetValueOrDefault("source");
var target = options.GetValueOrDefault("target");
var output = options.GetValueOrDefault("output") ?? target!;

if (source is null || target is null || !File.Exists(source) || !File.Exists(target))
{
    Console.Error.WriteLine("usage: Atom.ResourceTransfer --source <app.exe> --target <7z.sfx> [--output <branded.sfx>]");
    return 2;
}

if (!string.Equals(Path.GetFullPath(target), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
{
    File.Copy(target, output, overwrite: true);
}

var sourceModule = Native.LoadLibraryEx(source, IntPtr.Zero, Native.LoadLibraryAsDataFile);
if (sourceModule == IntPtr.Zero)
{
    Console.Error.WriteLine($"Could not load source resources: {Marshal.GetLastWin32Error()}");
    return 3;
}

try
{
    var copied = 0;
    foreach (var type in new[] { 3, 14, 16, 24 }) // RT_ICON, RT_GROUP_ICON, RT_VERSION, RT_MANIFEST
    {
        var entries = ReadEntries(sourceModule, type);
        if (entries.Count == 0)
        {
            continue;
        }

        var destination = Native.LoadLibraryEx(output, IntPtr.Zero, Native.LoadLibraryAsDataFile);
        if (destination == IntPtr.Zero)
        {
            Console.Error.WriteLine($"Could not load target resources: {Marshal.GetLastWin32Error()}");
            return 4;
        }

        var existing = ReadEntries(destination, type);
        Native.FreeLibrary(destination);

        var update = Native.BeginUpdateResource(output, false);
        if (update == IntPtr.Zero)
        {
            Console.Error.WriteLine($"Could not open target for update: {Marshal.GetLastWin32Error()}");
            return 5;
        }

        try
        {
            foreach (var item in existing)
            {
                Native.UpdateResource(update, new IntPtr(type), new IntPtr(item.Name), item.Language, IntPtr.Zero, 0);
            }

            foreach (var item in entries)
            {
                var data = ReadResource(sourceModule, type, item.Name, item.Language);
                if (data is null)
                {
                    continue;
                }

                var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
                try
                {
                    if (Native.UpdateResource(update, new IntPtr(type), new IntPtr(item.Name), item.Language,
                            handle.AddrOfPinnedObject(), (uint)data.Length))
                    {
                        copied++;
                    }
                }
                finally
                {
                    handle.Free();
                }
            }
        }
        finally
        {
            Native.EndUpdateResource(update, false);
        }
    }

    Console.WriteLine($"Copied {copied} resource entries into {output}");
    return copied > 0 ? 0 : 6;
}
finally
{
    Native.FreeLibrary(sourceModule);
}

static Dictionary<string, string> Parse(string[] arguments)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < arguments.Length - 1; i++)
    {
        if (arguments[i].StartsWith("--", StringComparison.Ordinal))
        {
            result[arguments[i][2..]] = arguments[++i];
        }
    }
    return result;
}

static List<ResourceEntry> ReadEntries(IntPtr module, int type)
{
    var entries = new List<ResourceEntry>();
    Native.EnumResourceNames(module, new IntPtr(type), (_, _, name, _) =>
    {
        if (!IsIntResource(name))
        {
            return true;
        }

        var id = (int)(long)name;
        Native.EnumResourceLanguages(module, new IntPtr(type), name, (_, _, _, language, _) =>
        {
            entries.Add(new ResourceEntry(id, language));
            return true;
        }, IntPtr.Zero);
        return true;
    }, IntPtr.Zero);
    return entries;
}

static byte[]? ReadResource(IntPtr module, int type, int name, ushort language)
{
    var info = Native.FindResourceEx(module, new IntPtr(type), new IntPtr(name), language);
    if (info == IntPtr.Zero)
    {
        return null;
    }

    var size = Native.SizeofResource(module, info);
    if (size == 0)
    {
        return null;
    }

    var handle = Native.LoadResource(module, info);
    var pointer = handle == IntPtr.Zero ? IntPtr.Zero : Native.LockResource(handle);
    if (pointer == IntPtr.Zero)
    {
        return null;
    }

    var data = new byte[size];
    Marshal.Copy(pointer, data, 0, (int)size);
    return data;
}

static bool IsIntResource(IntPtr name) => (ulong)name.ToInt64() >> 16 == 0;

readonly record struct ResourceEntry(int Name, ushort Language);

internal delegate bool EnumResNameProc(IntPtr module, IntPtr type, IntPtr name, IntPtr param);

internal delegate bool EnumResLangProc(IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr param);

internal static class Native
{
    public const uint LoadLibraryAsDataFile = 0x00000002;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResNameProc callback, IntPtr param);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumResourceLanguages(IntPtr module, IntPtr type, IntPtr name, EnumResLangProc callback, IntPtr param);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindResourceEx(IntPtr module, IntPtr type, IntPtr name, ushort language);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint SizeofResource(IntPtr module, IntPtr info);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LoadResource(IntPtr module, IntPtr info);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LockResource(IntPtr resource);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr BeginUpdateResource(string fileName, [MarshalAs(UnmanagedType.Bool)] bool deleteExisting);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language, IntPtr data, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EndUpdateResource(IntPtr update, [MarshalAs(UnmanagedType.Bool)] bool discard);
}

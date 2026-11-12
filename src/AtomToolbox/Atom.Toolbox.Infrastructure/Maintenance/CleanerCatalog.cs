using Atom.Toolbox.Application.Abstractions;
using Atom.Toolbox.Domain.Maintenance;

namespace Atom.Toolbox.Infrastructure.Maintenance;

public sealed class CleanerCatalog : ICleanerCatalog
{
    public IReadOnlyList<CleanerTask> Tasks { get; } =
    [
        new("win_temp", "WINDOWS TEMP", "Temp files from C:\\Windows\\Temp and your user %TEMP% folder"),
        new("prefetch", "PREFETCH", "Windows application prefetch cache"),
        new("recent", "RECENT ITEMS", "Recent files and folders history"),
        new("recycle_bin", "RECYCLE BIN", "Empty all Recycle Bins"),
        new("event_logs", "EVENT LOGS", "Clear the main Windows event logs"),
        new("dns_cache", "DNS CACHE", "Flush the DNS resolver cache"),
        new("wu_cache", "WINDOWS UPDATE CACHE", "SoftwareDistribution download cache"),
        new("thumb_cache", "THUMBNAIL CACHE", "Explorer thumbnail cache files"),
        new("crash_dumps", "CRASH DUMPS", "Windows minidumps and application crash dumps")
    ];
}

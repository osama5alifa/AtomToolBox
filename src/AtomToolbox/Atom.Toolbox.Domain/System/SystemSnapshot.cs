namespace Atom.Toolbox.Domain.System;

public sealed record SystemSnapshot(
    int CpuPercent,
    int MemoryUsedPercent,
    long MemoryTotalBytes,
    long MemoryFreeBytes,
    long DiskTotalBytes,
    long DiskFreeBytes,
    TimeSpan Uptime,
    string GpuName,
    string CpuName,
    string OsName,
    string OsVersion,
    string OsBuild,
    string HostName,
    int LogicalProcessors,
    bool IsElevated)
{
    public long DiskUsedBytes => Math.Max(0, DiskTotalBytes - DiskFreeBytes);

    public int DiskUsedPercent => DiskTotalBytes <= 0
        ? 0
        : (int)Math.Round(DiskUsedBytes * 100d / DiskTotalBytes);

    public static SystemSnapshot Empty { get; } = new(
        0, 0, 0, 0, 0, 0, TimeSpan.Zero, "--", "--", "Windows", "--", "--", "--", 0, false);
}

public sealed record SystemHeader(string CpuPercent, string GpuName, string OsBuild, string ProfileName);

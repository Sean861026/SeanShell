using System.Runtime.InteropServices;
using SeanShell.Core;

namespace SeanShell.Windows;

public sealed class SystemMetricsProvider
{
    private readonly object _gate = new();
    private ulong _previousIdle;
    private ulong _previousKernel;
    private ulong _previousUser;
    private bool _hasPreviousCpuSample;

    public SystemMetricsSnapshot Capture()
    {
        lock (_gate)
        {
            var cpuUsage = CaptureCpuUsage();
            var memory = new MemoryStatusEx();
            if (!GlobalMemoryStatusEx(ref memory))
            {
                Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
            }

            return new SystemMetricsSnapshot(
                cpuUsage,
                memory.TotalPhysical,
                memory.AvailablePhysical,
                DateTimeOffset.UtcNow);
        }
    }

    private double CaptureCpuUsage()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error());
        }

        var idle = idleTime.ToUInt64();
        var kernel = kernelTime.ToUInt64();
        var user = userTime.ToUInt64();

        if (!_hasPreviousCpuSample)
        {
            _previousIdle = idle;
            _previousKernel = kernel;
            _previousUser = user;
            _hasPreviousCpuSample = true;
            return 0;
        }

        var idleDelta = idle - _previousIdle;
        var totalDelta = (kernel - _previousKernel) + (user - _previousUser);
        _previousIdle = idle;
        _previousKernel = kernel;
        _previousUser = user;

        return totalDelta == 0
            ? 0
            : Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint LowDateTime;
        public uint HighDateTime;

        public readonly ulong ToUInt64() => ((ulong)HighDateTime << 32) | LowDateTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;

        public MemoryStatusEx()
        {
            Length = checked((uint)Marshal.SizeOf<MemoryStatusEx>());
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FileTime idleTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}

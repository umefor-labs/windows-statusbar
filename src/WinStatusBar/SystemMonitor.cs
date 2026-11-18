using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;

namespace WinStatusBar;

internal sealed record SystemSnapshot(
    double? Cpu, double RamUsedGb, double RamTotalGb, double? Gpu,
    double? CpuTemp, double? GpuTemp,
    double NetDown, double NetUp, double DiskRead, double DiskWrite);

/// <summary>Samples hardware once per call. Call from a background thread.</summary>
internal sealed class SystemMonitor : IDisposable
{
    public SystemMonitor(bool useSensorFile = true)
    {
        _useSensorFile = useSensorFile;
        _cpu = TryCounter("Processor Information", "% Processor Utility", "_Total");
        _diskRead = TryCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
        _diskWrite = TryCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");

        try
        {
            // GPU load/temp work without admin on NVIDIA; CPU temperature needs admin.
            _lhm = new Computer { IsCpuEnabled = true, IsGpuEnabled = true };
            _lhm.Open();
        }
        catch
        {
            _lhm = null;
        }
    }

    public SystemSnapshot Sample()
    {
        double? cpu = Read(_cpu) is double c ? Math.Min(c, 100) : null;

        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);
        double total = mem.ullTotalPhys / 1073741824.0;
        double used = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;

        double? gpu = null, gpuTemp = null, cpuTemp = null;
        if (_lhm != null)
        {
            try
            {
                foreach (var hw in _lhm.Hardware)
                {
                    if (hw.HardwareType == HardwareType.Cpu)
                    {
                        hw.Update();
                        cpuTemp = Sensor(hw, SensorType.Temperature, "CPU Package")
                                  ?? Sensor(hw, SensorType.Temperature, "Core Average")
                                  ?? Sensor(hw, SensorType.Temperature, null);
                    }
                    // Prefer the discrete GPU (NVIDIA/AMD) over the integrated one.
                    else if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)
                    {
                        hw.Update();
                        gpu = Sensor(hw, SensorType.Load, "GPU Core");
                        gpuTemp = Sensor(hw, SensorType.Temperature, "GPU Core");
                    }
                    else if (hw.HardwareType == HardwareType.GpuIntel && gpu == null)
                    {
                        hw.Update();
                        gpu = Sensor(hw, SensorType.Load, "D3D 3D") ?? Sensor(hw, SensorType.Load, null);
                    }
                }
            }
            catch
            {
                // A flaky sensor must never take the bar down.
            }
        }

        // Not elevated: CPU temperature comes from the elevated --sensors helper.
        if (cpuTemp == null && _useSensorFile) cpuTemp = SensorHost.ReadCpuTemp();

        var (down, up) = NetRates();

        return new SystemSnapshot(cpu, used, total, gpu, cpuTemp, gpuTemp, down, up,
            Read(_diskRead) ?? 0, Read(_diskWrite) ?? 0);
    }

    private (double down, double up) NetRates()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = ni.GetIPStatistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
        }
        catch
        {
            return (0, 0);
        }

        var now = Stopwatch.GetTimestamp();
        double down = 0, up = 0;
        if (_lastNetTs != 0)
        {
            double secs = (now - _lastNetTs) / (double)Stopwatch.Frequency;
            if (secs > 0)
            {
                down = Math.Max(0, (rx - _lastRx) / secs);
                up = Math.Max(0, (tx - _lastTx) / secs);
            }
        }
        _lastNetTs = now;
        _lastRx = rx;
        _lastTx = tx;
        return (down, up);
    }

    private static double? Sensor(IHardware hw, SensorType type, string? name)
    {
        foreach (var s in hw.Sensors)
            if (s.SensorType == type && (name == null || s.Name == name) && s.Value is float v)
                return v;
        return null;
    }

    private static PerformanceCounter? TryCounter(string cat, string counter, string instance)
    {
        try
        {
            var pc = new PerformanceCounter(cat, counter, instance, readOnly: true);
            pc.NextValue(); // first read is always 0
            return pc;
        }
        catch
        {
            return null;
        }
    }

    private static double? Read(PerformanceCounter? pc)
    {
        try { return pc?.NextValue(); }
        catch { return null; }
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        _diskRead?.Dispose();
        _diskWrite?.Dispose();
        try { _lhm?.Close(); } catch { }
    }

    private readonly PerformanceCounter? _cpu, _diskRead, _diskWrite;
    private readonly Computer? _lhm;
    private long _lastNetTs, _lastRx, _lastTx;
    private readonly bool _useSensorFile;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
            ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
}

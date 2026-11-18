using System.IO;
using System.Security.Principal;
using System.Text.Json;

namespace WinStatusBar;

/// <summary>
/// CPU temperature needs admin rights (PawnIO driver). The bar itself runs as a normal user so its
/// web logins work; a small elevated copy started with --sensors writes temperatures to a file it reads.
/// </summary>
internal static class SensorHost
{
    public static readonly string FilePath = Path.Combine(UsageService.DataDir, "sensors.json");

    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>Runs forever: sample temperatures every 2 s and publish them.</summary>
    public static void Run()
    {
        Directory.CreateDirectory(UsageService.DataDir);
        using var monitor = new SystemMonitor(useSensorFile: false);
        var tmp = FilePath + ".tmp";
        while (true)
        {
            try
            {
                var s = monitor.Sample();
                File.WriteAllText(tmp, JsonSerializer.Serialize(new Reading(s.CpuTemp, s.GpuTemp, DateTimeOffset.UtcNow)));
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch
            {
                // Keep going; the bar treats a stale file as "no reading".
            }
            Thread.Sleep(2000);
        }
    }

    /// <summary>Latest CPU temperature published by the elevated helper, if fresh.</summary>
    public static double? ReadCpuTemp()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var r = JsonSerializer.Deserialize<Reading>(File.ReadAllText(FilePath));
            if (r == null || DateTimeOffset.UtcNow - r.At > TimeSpan.FromSeconds(15)) return null;
            return r.CpuTemp;
        }
        catch
        {
            return null;
        }
    }

    private sealed record Reading(double? CpuTemp, double? GpuTemp, DateTimeOffset At);
}

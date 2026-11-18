using System.IO;
using System.Management;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinStatusBar;

/// <summary>
/// Reads model quotas from Antigravity's local language server (only while Antigravity is open).
/// Models whose quota resets within a day form the short (5-hour) bar; the rest form the weekly bar.
/// </summary>
internal static class AntigravityUsage
{
    public static string? ExePath()
    {
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("Antigravity"))
                if (p.MainModule?.FileName is string f) return f;
        }
        catch { }
        var guess = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "antigravity", "Antigravity.exe");
        return File.Exists(guess) ? guess : null;
    }

    public static async Task<UsageResult?> FetchAsync()
    {
        var server = FindServer();
        if (server == null) return null; // not running: caller keeps the last value

        var (pid, token) = server.Value;
        var ports = ListeningPorts(pid);
        if (_port is int cached) ports.Insert(0, cached);

        foreach (var port in ports.Distinct())
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post,
                    $"http://127.0.0.1:{port}/exa.language_server_pb.LanguageServerService/GetUserStatus");
                req.Headers.Add("X-Codeium-Csrf-Token", token);
                req.Headers.Add("Connect-Protocol-Version", "1");
                req.Content = new StringContent(
                    """{"metadata":{"ideName":"antigravity","extensionName":"antigravity","locale":"en"}}""",
                    Encoding.UTF8, "application/json");
                using var res = await Http.SendAsync(req);
                if (!res.IsSuccessStatusCode) continue;
                _port = port;
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                return Parse(doc.RootElement);
            }
            catch
            {
                // Wrong port (some speak TLS or another protocol); try the next.
            }
        }
        return new UsageResult(true, null, null, null, null, "không đọc được dịch vụ Antigravity");
    }

    private static UsageResult Parse(JsonElement root)
    {
        double? shortUsed = null, longUsed = null;
        DateTimeOffset? shortReset = null, longReset = null;
        var now = DateTimeOffset.UtcNow;

        if (root.TryGetProperty("userStatus", out var us)
            && us.TryGetProperty("cascadeModelConfigData", out var cfg)
            && cfg.TryGetProperty("clientModelConfigs", out var models)
            && models.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in models.EnumerateArray())
            {
                if (!m.TryGetProperty("quotaInfo", out var q)) continue;
                if (!q.TryGetProperty("resetTime", out var rt) || !DateTimeOffset.TryParse(rt.GetString(), out var reset)) continue;
                // remainingFraction is omitted when 0 (proto3 default).
                double remaining = q.TryGetProperty("remainingFraction", out var rf) && rf.ValueKind == JsonValueKind.Number ? rf.GetDouble() : 0;
                double used = Math.Clamp((1 - remaining) * 100, 0, 100);

                if (reset - now <= TimeSpan.FromHours(24))
                {
                    if (shortUsed == null || used > shortUsed) { shortUsed = used; shortReset = reset; }
                }
                else if (longUsed == null || used > longUsed)
                {
                    longUsed = used;
                    longReset = reset;
                }
            }
        }
        return new UsageResult(true, shortUsed, longUsed, shortReset, longReset);
    }

    private static (int pid, string token)? FindServer()
    {
        try
        {
            using var s = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'language_server.exe'");
            foreach (ManagementObject o in s.Get())
            {
                var cmd = o["CommandLine"] as string ?? "";
                if (!cmd.Contains("antigravity", StringComparison.OrdinalIgnoreCase)) continue;
                var m = Regex.Match(cmd, @"--csrf_token[= ]+(\S+)");
                if (m.Success) return (Convert.ToInt32(o["ProcessId"]), m.Groups[1].Value.Trim('"'));
            }
        }
        catch { }
        return null;
    }

    private static List<int> ListeningPorts(int pid)
    {
        var ports = new List<int>();
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2 /* AF_INET */, 3 /* TCP_TABLE_OWNER_PID_LISTENER */, 0);
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, 2, 3, 0) != 0) return ports;
            int count = Marshal.ReadInt32(buf);
            int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(buf + 4 + i * rowSize);
                if (row.owningPid == pid)
                    ports.Add(((int)row.localPort & 0xFF) << 8 | ((int)row.localPort >> 8 & 0xFF));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
        // Highest ports first: that is where the HTTP endpoint has been observed.
        ports.Sort((a, b) => b.CompareTo(a));
        return ports;
    }

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private static int? _port;

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint state, localAddr, localPort, remoteAddr, remotePort, owningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, uint reserved);
}

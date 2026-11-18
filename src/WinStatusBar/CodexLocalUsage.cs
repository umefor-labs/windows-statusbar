using System.IO;
using System.Text;
using System.Text.Json;

namespace WinStatusBar;

/// <summary>
/// Reads Codex plan limits from Codex's own session logs (~/.codex/sessions), which record
/// rate_limits after every turn. No login needed; the numbers refresh whenever Codex is used.
/// </summary>
internal static class CodexLocalUsage
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");

    public static UsageResult? Fetch()
    {
        try
        {
            foreach (var file in RecentFiles())
            {
                var r = LastRateLimits(file);
                if (r != null) return r;
            }
        }
        catch (Exception ex)
        {
            UsageService.Log($"Codex local: {ex.Message}");
        }
        return null;
    }

    /// <summary>Session files from the last 8 days, newest first (sessions/yyyy/MM/dd/*.jsonl).</summary>
    private static IEnumerable<string> RecentFiles()
    {
        var files = new List<FileInfo>();
        for (int d = 0; d < 8; d++)
        {
            var day = DateTime.Now.Date.AddDays(-d);
            var dir = Path.Combine(Root, day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
            if (Directory.Exists(dir))
                files.AddRange(new DirectoryInfo(dir).GetFiles("*.jsonl"));
        }
        return files.OrderByDescending(f => f.LastWriteTimeUtc).Take(10).Select(f => f.FullName);
    }

    private static UsageResult? LastRateLimits(string path)
    {
        const int Chunk = 1024 * 1024;
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long start = Math.Max(0, fs.Length - Chunk);
        fs.Seek(start, SeekOrigin.Begin);
        var buf = new byte[fs.Length - start];
        int read = 0;
        while (read < buf.Length)
        {
            int n = fs.Read(buf, read, buf.Length - read);
            if (n == 0) break;
            read += n;
        }
        var lines = Encoding.UTF8.GetString(buf, 0, read).Split('\n');
        for (int i = lines.Length - 1; i >= (start > 0 ? 1 : 0); i--)
        {
            var line = lines[i];
            if (!line.Contains("\"rate_limits\"")) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("payload", out var p)
                    || !p.TryGetProperty("rate_limits", out var rl)
                    || rl.ValueKind != JsonValueKind.Object) continue;
                var (s, sr) = Window(rl, "primary");
                var (l, lr) = Window(rl, "secondary");
                return new UsageResult(true, s, l, sr, lr);
            }
            catch (JsonException)
            {
            }
        }
        return null;
    }

    /// <summary>Used percent and reset time; a window whose reset has passed counts as 0% used.</summary>
    private static (double? used, DateTimeOffset? reset) Window(JsonElement rl, string name)
    {
        if (!rl.TryGetProperty(name, out var w) || w.ValueKind != JsonValueKind.Object) return (null, null);
        double? used = w.TryGetProperty("used_percent", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetDouble() : null;
        DateTimeOffset? reset = w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(r.GetInt64()) : null;
        if (reset is DateTimeOffset t && t <= DateTimeOffset.Now) return (0, null);
        return (used, reset);
    }
}

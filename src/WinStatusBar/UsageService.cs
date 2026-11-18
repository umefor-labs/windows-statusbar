using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace WinStatusBar;

internal sealed record UsageResult(bool SignedIn, double? Short, double? Long,
    DateTimeOffset? ShortReset, DateTimeOffset? LongReset, string? Error = null);

/// <summary>A service whose plan limits we read from its own website, using the user's web login.</summary>
internal sealed record UsageProvider(string Name, string Origin, string LoginUrl, string Script,
    Func<JsonElement, UsageResult> Parse);

/// <summary>
/// Reads plan usage by running fetch() inside an invisible WebView2 that shares the login
/// cookies of the sign-in window. Nothing leaves the machine except the calls the site itself makes.
/// </summary>
internal sealed class UsageService
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinStatusBar");

    private static Task<CoreWebView2Environment>? _env;

    public static Task<CoreWebView2Environment> GetEnvironmentAsync() =>
        _env ??= CoreWebView2Environment.CreateAsync(null, Path.Combine(DataDir, "WebView2"));

    public UsageService(IntPtr parentHwnd) => _parent = parentHwnd;

    public async Task<UsageResult> FetchAsync(UsageProvider p)
    {
        await _gate.WaitAsync();
        try
        {
            var core = await EnsureAsync();

            var navigated = new TaskCompletionSource<bool>();
            void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs e) => navigated.TrySetResult(e.IsSuccess);
            core.NavigationCompleted += OnNav;
            core.Navigate(p.Origin + "/robots.txt");
            await Task.WhenAny(navigated.Task, Task.Delay(20000));
            core.NavigationCompleted -= OnNav;
            Log($"{p.Name}: navigated={(navigated.Task.IsCompleted ? navigated.Task.Result.ToString() : "timeout")}");

            var message = new TaskCompletionSource<string>();
            void OnMsg(object? s, CoreWebView2WebMessageReceivedEventArgs e) => message.TrySetResult(e.TryGetWebMessageAsString());
            core.WebMessageReceived += OnMsg;
            var exec = core.ExecuteScriptAsync(p.Script);
            var execDone = await Task.WhenAny(exec, Task.Delay(20000));
            Log($"{p.Name}: script {(execDone == exec ? "started" : "did not start")}");
            var done = await Task.WhenAny(message.Task, Task.Delay(20000));
            core.WebMessageReceived -= OnMsg;

            if (done != message.Task)
            {
                Log($"{p.Name}: no answer from page (navigated={navigated.Task.IsCompleted})");
                return new UsageResult(false, null, null, null, null, "timeout");
            }

            using var doc = JsonDocument.Parse(message.Task.Result);
            var root = doc.RootElement;
            if (!root.GetProperty("ok").GetBoolean())
            {
                int status = root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : 0;
                bool signedOut = status is 401 or 403 || status == 0;
                Log($"{p.Name}: page answered not-ok: {message.Task.Result}");
                return new UsageResult(!signedOut, null, null, null, null,
                    root.TryGetProperty("e", out var err) ? err.ToString() : $"HTTP {status}");
            }
            var data = root.GetProperty("d");
            try { File.WriteAllText(Path.Combine(DataDir, $"usage-{p.Name.ToLowerInvariant()}.json"), data.GetRawText()); } catch { }
            return p.Parse(data);
        }
        catch (Exception ex)
        {
            Log($"{p.Name}: {ex}");
            return new UsageResult(false, null, null, null, null, ex.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            File.AppendAllText(Path.Combine(DataDir, "error.log"), $"{DateTime.Now:s} {line}\n");
        }
        catch { }
    }

    private async Task<CoreWebView2> EnsureAsync()
    {
        if (_controller != null) return _controller.CoreWebView2;
        Directory.CreateDirectory(DataDir);
        Log("creating WebView2 environment");
        var env = await GetEnvironmentAsync();
        Log($"environment ok ({env.BrowserVersionString}); creating controller");
        _controller = await env.CreateCoreWebView2ControllerAsync(_parent);
        Log("controller ok");
        _controller.IsVisible = false;
        return _controller.CoreWebView2;
    }

    private readonly IntPtr _parent;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CoreWebView2Controller? _controller;

    // ---------- Providers ----------

    private const string Post = "m=>chrome.webview.postMessage(JSON.stringify(m))";

    public static readonly UsageProvider Claude = new(
        "Claude", "https://claude.ai", "https://claude.ai/login",
        $$$"""
        (async()=>{const send={{{Post}}};try{
          const r=await fetch('/api/organizations',{credentials:'include'});
          if(!r.ok){send({ok:false,status:r.status});return;}
          const orgs=await r.json();
          const org=(orgs.find(o=>(o.capabilities||[]).includes('chat'))||orgs[0]).uuid;
          const u=await fetch(`/api/organizations/${org}/usage`,{credentials:'include'});
          if(!u.ok){send({ok:false,status:u.status});return;}
          send({ok:true,d:await u.json()});
        }catch(e){send({ok:false,e:String(e)});}})();
        """,
        d => new UsageResult(true,
            Num(d, "five_hour", "utilization"), Num(d, "seven_day", "utilization"),
            Iso(d, "five_hour", "resets_at"), Iso(d, "seven_day", "resets_at")));

    public static readonly UsageProvider Codex = new(
        "Codex", "https://chatgpt.com", "https://chatgpt.com/auth/login",
        $$$"""
        (async()=>{const send={{{Post}}};try{
          const s=await fetch('/api/auth/session',{credentials:'include'});
          const j=s.ok?await s.json():{};
          if(!j.accessToken){send({ok:false,status:401});return;}
          const h={Authorization:'Bearer '+j.accessToken};
          if(j.account&&j.account.id)h['ChatGPT-Account-Id']=j.account.id;
          const u=await fetch('/backend-api/wham/usage',{headers:h,credentials:'include'});
          if(!u.ok){send({ok:false,status:u.status});return;}
          const d=await u.json();
          send({ok:true,d:{rate_limit:d.rate_limit,plan_type:d.plan_type}});
        }catch(e){send({ok:false,e:String(e)});}})();
        """,
        d =>
        {
            var rl = d.TryGetProperty("rate_limit", out var r) ? r : default;
            return new UsageResult(true,
                Num(rl, "primary_window", "used_percent"), Num(rl, "secondary_window", "used_percent"),
                Unix(rl, "primary_window", "reset_at"), Unix(rl, "secondary_window", "reset_at"));
        });

    private static JsonElement? Get(JsonElement e, string a, string b) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(a, out var x) && x.ValueKind == JsonValueKind.Object
        && x.TryGetProperty(b, out var y) ? y : null;

    private static double? Num(JsonElement e, string a, string b) =>
        Get(e, a, b) is { ValueKind: JsonValueKind.Number } v ? v.GetDouble() : null;

    private static DateTimeOffset? Iso(JsonElement e, string a, string b) =>
        Get(e, a, b) is { ValueKind: JsonValueKind.String } v && DateTimeOffset.TryParse(v.GetString(), out var t) ? t : null;

    private static DateTimeOffset? Unix(JsonElement e, string a, string b) =>
        Get(e, a, b) is { ValueKind: JsonValueKind.Number } v ? DateTimeOffset.FromUnixTimeSeconds(v.GetInt64()) : null;
}

using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ManakaVR.Configurator;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
bool noBrowser = args.Contains("--no-browser");
string? explicitRoot = Arg("--game-root");
string? root = explicitRoot;
if (root == null)
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "SecretFlasherManaka.exe"))) { root = dir.FullName; break; }
}
if (root == null || !Directory.Exists(root))
{
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "启动说明.txt"), "请将此工具文件夹放在游戏目录下，或使用 --game-root 指定游戏目录。无需进入游戏或选择配置文件。");
    if (!noBrowser) Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "启动说明.txt")) { UseShellExecute = true });
    return;
}
root = Path.GetFullPath(root);
string configPath = Path.Combine(root, "BepInEx", "config", "com.codex.secretflashermanaka.vr.cfg");
string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..16];
string sessionDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ManakaVRConfigurator", key);
Directory.CreateDirectory(sessionDir);
string sessionFile = Path.Combine(sessionDir, "session.json");
FileStream instanceLock;
try { instanceLock = new FileStream(Path.Combine(sessionDir, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
catch (IOException)
{
    for (int i = 0; i < 20; i++)
    {
        try
        {
            using var session = JsonDocument.Parse(File.ReadAllText(sessionFile));
            string url = session.RootElement.GetProperty("url").GetString()!;
            // The lock proves an instance is alive; only accept a local session URL.
            var uri = new Uri(url);
            if (uri.Host == "127.0.0.1" && uri.Scheme == "http")
            { if (!noBrowser) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return; }
        }
        catch (IOException) { }
        catch (JsonException) { }
        await Task.Delay(150);
    }
    return;
}
using (instanceLock)
{
    if (File.Exists(sessionFile)) File.Delete(sessionFile);
    string Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ManakaVR.Configurator." + name)
            ?? throw new FileNotFoundException(name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    bool Running()
    {
        foreach (var p in Process.GetProcessesByName("SecretFlasherManaka"))
        {
            using (p)
            {
                try { if (string.Equals(Path.GetDirectoryName(p.MainModule?.FileName), root, StringComparison.OrdinalIgnoreCase)) return true; }
                catch { return true; } // Inaccessible running game: defer saving rather than overwrite it.
            }
        }
        return false;
    }
    var store = new ConfigStore(configPath, Resource("Data.defaults.cfg"), Running);
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    string prefix = "/s/" + token;
    var builder = WebApplication.CreateSlimBuilder(args: []);
    builder.Logging.ClearProviders();
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.Listen(IPAddress.Loopback, 0);
        options.Limits.MaxRequestBodySize = 512 * 1024;
    });
    var app = builder.Build();
    DateTime lastContact = DateTime.UtcNow;
    string origin = "";
    app.Use(async (context, next) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'none'";
        if (context.Request.Host.Host != "127.0.0.1" || !context.Request.Path.StartsWithSegments(prefix))
        { context.Response.StatusCode = 403; return; }
        if (context.Request.Method != "GET" &&
            (context.Request.Headers["X-Config-Token"] != token ||
             (context.Request.Headers.Origin.Count > 0 && context.Request.Headers.Origin != origin)))
        { context.Response.StatusCode = 403; return; }
        lastContact = DateTime.UtcNow;
        try { await next(context); }
        catch (ConfigConflictException e) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = e.Message }); }
        catch (Exception e) when (e is InvalidDataException or JsonException or BadHttpRequestException)
        { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = e.Message }); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { error = "无法访问配置文件：" + e.Message }); }
    });
    app.MapGet(prefix + "/", () => Results.Content(Resource("Web.index.html"), "text/html; charset=utf-8"));
    app.MapGet(prefix + "/app.js", () => Results.Content(Resource("Web.app.js"), "text/javascript; charset=utf-8"));
    app.MapGet(prefix + "/style.css", () => Results.Content(Resource("Web.style.css"), "text/css; charset=utf-8"));
    app.MapGet(prefix + "/api/config", () => Results.Json(new
    {
        snapshot = store.Read(), gameRoot = root, configPath, gameRunning = Running(),
        labels = JsonSerializer.Deserialize<JsonElement>(Resource("Data.labels.json")),
        fixedSettings = JsonSerializer.Deserialize<JsonElement>(Resource("Data.fixed.json"))
    }));
    app.MapPost(prefix + "/api/config", (SaveRequest request) =>
    {
        var saved = store.Save(request);
        return Results.Json(new { snapshot = saved.Snapshot, backup = saved.Backup });
    });
    string draftPath = Path.Combine(sessionDir, "draft.json");
    var draftGate = new object();
    app.MapGet(prefix + "/api/draft", () =>
    {
        lock (draftGate) return File.Exists(draftPath)
            ? Results.Text(File.ReadAllText(draftPath), "application/json") : Results.Text("null", "application/json");
    });
    app.MapPost(prefix + "/api/draft", (SaveRequest draft) =>
    {
        if (draft.Changes == null || draft.Changes.Count > 1000 || draft.Changes.Any(p => p.Value == null || p.Value.Length > 16384))
            throw new InvalidDataException("草稿格式无效。");
        lock (draftGate)
        {
            string temp = draftPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(draft, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            File.Move(temp, draftPath, true);
        }
        return Results.Ok();
    });
    app.MapGet(prefix + "/api/backups", () => Results.Json(store.Backups()));
    app.MapGet(prefix + "/api/backups/{name}", (string name) => Results.Json(store.ReadBackup(name)));
    app.MapGet(prefix + "/api/status", () =>
    {
        string statusPath = Path.Combine(root, "BepInEx", "config", "ManakaVRBody", "status.json");
        bool running = Running();
        JsonElement? body = null;
        bool fresh = false;
        try
        {
            if (File.Exists(statusPath))
            {
                body = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(statusPath));
                fresh = running && (DateTime.UtcNow - File.GetLastWriteTimeUtc(statusPath)).TotalSeconds < 5;
            }
        }
        catch (IOException) { }
        catch (JsonException) { }
        return Results.Json(new { gameRunning = running, fresh, body = fresh ? body : null });
    });
    app.MapPost(prefix + "/api/quit", () =>
    {
        _ = Task.Run(async () => { await Task.Delay(300); app.Lifetime.StopApplication(); });
        return Results.Ok();
    });
    await app.StartAsync();
    origin = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    string appUrl = origin + prefix + "/";
    await File.WriteAllTextAsync(sessionFile, JsonSerializer.Serialize(new { url = appUrl, pid = Environment.ProcessId, gameRoot = root }));
    if (!noBrowser) Process.Start(new ProcessStartInfo(appUrl) { UseShellExecute = true });
    // Closing a browser tab leaves no permanent service behind. Hidden tabs can be
    // throttled heavily, so allow 30 minutes without any heartbeat before exiting.
    using var timer = new Timer(_ => { if (DateTime.UtcNow - lastContact > TimeSpan.FromMinutes(30)) app.Lifetime.StopApplication(); }, null, 60000, 60000);
    await app.WaitForShutdownAsync();
    if (File.Exists(sessionFile)) File.Delete(sessionFile);
}

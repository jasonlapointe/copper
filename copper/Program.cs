using System.Text;
using System.Text.Json;
using Copper;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();

AppConfig config;
try
{
    config = AppConfig.Load();
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

var hub = new SseHub();
var waState = "connecting";
var brainState = "checking";
const string BrainHelp = "Copper's brain isn't signed in. In a terminal run: claude login — then restart Copper.";

var wa = new WaService(config.BridgeDir, config.Contact.ChatName);
wa.OnLiveMessage += (id, body, media) => hub.Broadcast(new { type = "incoming", id, body, media });

var logger = new TurnLogger(config.BaseDir);
// The CLI runs in the bridge dir so it can read downloaded images under media/.
var store = new TranslationStore(config.BaseDir, config.ContactSlug);
var agent = new Agent(config, wa, new ClaudeCli(config.BridgeDir), logger, store);
agent.OnStatus += s => hub.Broadcast(new { type = "status", text = s });

// Connect WhatsApp in the background so the UI comes up immediately.
_ = Task.Run(async () =>
{
    try
    {
        await wa.StartAsync(TimeSpan.FromMinutes(3));
        waState = "connected";
    }
    catch (Exception ex)
    {
        waState = "failed: " + ex.Message;
    }
    hub.Broadcast(new { type = "wa", state = waState });
});

// Brain health probe: fail loudly at startup instead of silently per message.
_ = Task.Run(async () =>
{
    try
    {
        await agent.PingAsync();
        brainState = "ok";
        hub.Broadcast(new { type = "brain", state = "ok" });
        // Groundings older than 7 days (or with heavy activity) get one LLM rewrite each.
        await agent.RefreshStaleGroundingsAsync(TimeSpan.FromDays(7));
    }
    catch (Exception ex)
    {
        brainState = "failed";
        hub.Broadcast(new { type = "brain", state = "failed", help = BrainHelp, detail = ex.Message });
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/state", () => Results.Json(new
{
    wa = waState,
    contact = config.Contact.Name,
    chat = config.Contact.ChatName,
}));

app.MapGet("/api/events", (HttpContext ctx) => hub.ServeAsync(ctx));

app.MapGet("/api/history", async (int? limit) =>
{
    try
    {
        var raw = await wa.ReadRawAsync(limit ?? 30);
        // Enrich Jason's own (ME) messages with the English he typed + back-translation, so his
        // bubbles read in English with the Russian available only on tap.
        var messages = raw.EnumerateArray().Select(m =>
        {
            var who = m.GetProperty("who").GetString();
            var body = m.GetProperty("body").GetString() ?? "";
            string? english = null, back = null;
            if (who == "ME" && store.TryGetSent(body, out var s)) { english = s.English; back = s.Back; }
            return new
            {
                id = m.TryGetProperty("id", out var i) ? i.GetString() : null,
                t = m.GetProperty("t").GetInt64(),
                who,
                body,
                media = m.TryGetProperty("media", out var md) && md.ValueKind == JsonValueKind.String ? md.GetString() : null,
                english,
                back,
            };
        });
        return Results.Json(new { messages });
    }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }); }
});

app.MapGet("/api/chats", async () =>
{
    try { return Results.Json(new { chats = await wa.ChatsAsync(20) }); }
    catch (Exception ex) { return Results.Json(new { error = ex.Message }); }
});

app.MapGet("/api/media/{name}", (string name) =>
{
    if (name.Contains("..") || name.Contains('/') || name.Contains('\\')) return Results.NotFound();
    var file = Path.Combine(config.BridgeDir, "media", name);
    if (!File.Exists(file)) return Results.NotFound();
    var mime = Path.GetExtension(file).TrimStart('.').ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "png" => "image/png",
        "webp" => "image/webp",
        "gif" => "image/gif",
        _ => "application/octet-stream",
    };
    return Results.File(file, mime);
});

// Jason's English → outgoing draft (LLM, single shot). Nothing is sent here.
app.MapPost("/api/message", async (HttpRequest req) =>
{
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body);
    var text = body.GetProperty("text").GetString()?.Trim() ?? "";
    if (text.Length == 0) return Results.Json(new { error = "empty message" });
    if (brainState == "failed") return Results.Json(new { error = BrainHelp });
    try
    {
        var outcome = await agent.ProcessAsync(text);
        // A completed exchange is when personas move; the refresh gates itself on age/activity.
        if (outcome.Sent is not null) _ = Task.Run(() => agent.RefreshStaleGroundingsAsync(TimeSpan.FromDays(7)));
        return Results.Json(new
        {
            sent = outcome.Sent is { } s ? new { english = s.English, russian = s.Russian, back = s.Back } : null,
            ack = outcome.Ack,
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message });
    }
});
// Send a photo to the contact, with an optional English caption rendered to his language.
app.MapPost("/api/photo", async (HttpRequest req) =>
{
    if (!req.HasFormContentType) return Results.Json(new { error = "expected multipart form" });
    var form = await req.ReadFormAsync();
    var file = form.Files.GetFile("photo");
    if (file is null || file.Length == 0) return Results.Json(new { error = "no photo" });
    var caption = form["caption"].ToString().Trim();

    // Save into the bridge's outgoing dir (bridge reads the path locally).
    var outDir = Path.Combine(config.BridgeDir, "outgoing");
    Directory.CreateDirectory(outDir);
    var ext = Path.GetExtension(file.FileName);
    if (string.IsNullOrEmpty(ext)) ext = ".jpg";
    var savedName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
    var savedPath = Path.Combine(outDir, savedName);
    await using (var fs = File.Create(savedPath)) await file.CopyToAsync(fs);

    try
    {
        var (russianCaption, back) = ("", "");
        if (caption.Length > 0 && brainState != "failed")
        {
            var rendered = await agent.RenderCaptionAsync(caption);
            russianCaption = rendered.Russian;
            back = rendered.Back;
        }
        await wa.SendMediaAsync(savedPath, russianCaption.Length > 0 ? russianCaption : null);
        return Results.Json(new
        {
            ok = true,
            url = "/api/outgoing/" + savedName,
            caption_en = caption.Length > 0 ? caption : null,
            caption_ru = russianCaption.Length > 0 ? russianCaption : null,
            back = back.Length > 0 ? back : null,
        });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message });
    }
});

app.MapGet("/api/outgoing/{name}", (string name) =>
{
    if (name.Contains("..") || name.Contains('/') || name.Contains('\\')) return Results.NotFound();
    var file = Path.Combine(config.BridgeDir, "outgoing", name);
    if (!File.Exists(file)) return Results.NotFound();
    var mime = Path.GetExtension(file).TrimStart('.').ToLowerInvariant() switch
    {
        "png" => "image/png", "webp" => "image/webp", "gif" => "image/gif", _ => "image/jpeg",
    };
    return Results.File(file, mime);
});

// Incoming messages → contextual English (LLM, single shot).
app.MapPost("/api/translate", async (HttpRequest req) =>
{
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body);
    List<(int N, string Id, string Body, string? Media)> messages = [];
    foreach (var m in body.GetProperty("messages").EnumerateArray())
        messages.Add((
            m.GetProperty("n").GetInt32(),
            m.TryGetProperty("id", out var mid) && mid.ValueKind == JsonValueKind.String ? mid.GetString() ?? "" : "",
            m.GetProperty("body").GetString() ?? "",
            m.TryGetProperty("media", out var med) && med.ValueKind == JsonValueKind.String ? med.GetString() : null));

    // Serve anything already translated from the local store — instant, free, no model call.
    var cached = agent.Cached(messages.Select(m => (m.N, m.Id)).ToList());
    var cachedNs = cached.Select(c => c.N).ToHashSet();
    var todo = messages.Where(m => !cachedNs.Contains(m.N)).ToList();

    if (todo.Count == 0)
        return Results.Json(new { translations = cached.Select(t => new { t.N, t.English, t.Note }) });
    if (brainState == "failed")
        return Results.Json(new { translations = cached.Select(t => new { t.N, t.English, t.Note }), error = BrainHelp });
    try
    {
        var fresh = await agent.TranslateAsync(todo);
        var all = cached.Concat(fresh);
        return Results.Json(new { translations = all.Select(t => new { t.N, t.English, t.Note }) });
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message });
    }
});

app.Lifetime.ApplicationStopping.Register(() => wa.Dispose());

Console.WriteLine($"copper UI → http://localhost:5077  (contact: {config.Contact.Name})");
app.Run("http://localhost:5077");
return 0;

using System.Text.Json;
using CopperBot;

var cfg = Config.FromEnvironment();
var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
var tg = new Telegram(cfg.TelegramToken, http);
var people = new People(cfg.DataDir);
var brain = new Brain(cfg, http);

var me = await tg.GetMeUsernameAsync();
Console.WriteLine($"copper-bot up as @{me} · languages: {string.Join("/", cfg.Langs)} · model: {cfg.Model}");

// Core: translate one incoming message and reply in-thread. Shared by webhook and long-poll.
async Task HandleUpdate(JsonElement update)
{
    if (!update.TryGetProperty("message", out var m)) return;
    if (!m.TryGetProperty("text", out var textEl)) return;      // MVP: text only (media is a TODO)
    var text = textEl.GetString() ?? "";
    if (text.Length == 0 || text.StartsWith('/')) return;        // skip commands like /start

    var chatId = m.GetProperty("chat").GetProperty("id").GetInt64();
    var from = m.GetProperty("from");
    var userId = from.GetProperty("id").GetInt64().ToString();
    var name = string.Join(" ", new[] { Get(from, "first_name"), Get(from, "last_name") }.Where(s => s.Length > 0));
    if (name.Length == 0) name = Get(from, "username");
    var replyTo = m.TryGetProperty("message_id", out var mid) ? mid.GetInt32() : (int?)null;

    try
    {
        var r = await brain.TranslateAsync(text, name, people.ContextBlock());
        // Auto-seed the sender's language on first contact (decision #40).
        if (!people.Knows(userId) && r.DetectedLang.Length > 0)
            people.Seed(userId, name, r.DetectedLang);

        // Only post a translation if it actually changed the language (don't echo same-language text).
        if (r.Text.Length > 0 && !r.Text.Equals(text, StringComparison.OrdinalIgnoreCase))
            await tg.SendMessageAsync(chatId, r.Text, replyTo);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"handle error: {ex.Message}");
    }
}

static string Get(JsonElement e, string k) => e.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";

if (cfg.PublicUrl is { Length: > 0 } url)
{
    // ── Cloud mode: webhook. Telegram POSTs updates to us; Cloud Run wakes on demand. ──
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    var app = builder.Build();

    app.MapGet("/", () => "copper-bot ok");
    app.MapPost("/telegram/webhook", async (HttpRequest req) =>
    {
        var update = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body);
        _ = HandleUpdate(update); // ack Telegram immediately; process in the background
        return Results.Ok();
    });

    await tg.SetWebhookAsync($"{url.TrimEnd('/')}/telegram/webhook");
    Console.WriteLine($"webhook set → {url}/telegram/webhook");
    app.Run($"http://0.0.0.0:{cfg.Port}");
}
else
{
    // ── Local mode: long-poll. No public URL needed — perfect for testing with family first. ──
    await tg.DeleteWebhookAsync(); // long-poll and webhook are mutually exclusive
    Console.WriteLine("long-poll mode (local). Message the bot in Telegram; Ctrl+C to stop.");
    long offset = 0;
    while (true)
    {
        try
        {
            var updates = await tg.GetUpdatesAsync(offset);
            foreach (var u in updates.EnumerateArray())
            {
                offset = u.GetProperty("update_id").GetInt64() + 1;
                await HandleUpdate(u);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"poll error: {ex.Message}");
            await Task.Delay(2000);
        }
    }
}

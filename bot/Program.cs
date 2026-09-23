using System.Text.Json;
using CopperBot;

var cfg = Config.FromEnvironment();
var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
var tg = new Telegram(cfg.TelegramToken, http);
var people = new People(cfg.GcpProject);
var brain = new Brain(cfg, http);

// Core: translate one incoming message and reply in-thread. Never throws — logs and moves on.
async Task HandleUpdate(JsonElement update)
{
    try
    {
        if (update.ValueKind != JsonValueKind.Object) return;
        if (!update.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object) return;
        if (!m.TryGetProperty("text", out var textEl) || textEl.ValueKind != JsonValueKind.String) return; // text only (MVP)
        var text = textEl.GetString() ?? "";
        if (text.Length == 0 || text.StartsWith('/')) return;             // skip commands like /start

        if (!m.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdEl)) return;
        var chatId = chatIdEl.GetInt64();
        // Operational log (no message content): lets us discover chat ids to put on the allowlist.
        var chatType = chat.TryGetProperty("type", out var ct) ? ct.GetString() : "?";
        Console.WriteLine($"incoming: chat={chatId} type={chatType} allowed={cfg.ChatAllowed(chatId)}");
        if (!cfg.ChatAllowed(chatId)) return;                             // allowlist bounds cost/abuse

        if (!m.TryGetProperty("from", out var from) || from.ValueKind != JsonValueKind.Object) return;
        if (from.TryGetProperty("is_bot", out var isBot) && isBot.ValueKind == JsonValueKind.True) return; // no bot↔bot loops
        if (!from.TryGetProperty("id", out var fromId)) return;
        var userId = fromId.GetInt64().ToString();
        var name = string.Join(" ", new[] { Get(from, "first_name"), Get(from, "last_name") }.Where(s => s.Length > 0));
        if (name.Length == 0) name = Get(from, "username");
        if (name.Length == 0) name = "unknown";
        var replyTo = m.TryGetProperty("message_id", out var mid) ? mid.GetInt32() : (int?)null;

        // Context scoped to THIS chat's participants only (privacy + bounded cost).
        var r = await brain.TranslateAsync(text, name, await people.ContextBlockAsync(chatId));

        // Auto-seed the sender on first contact in this chat (decision #40).
        if (r.DetectedLang.Length > 0)
            await people.SeedAsync(chatId, userId, name, r.DetectedLang);

        // Post only if the language actually changed (don't echo same-language text).
        if (r.Text.Length > 0 && !r.Text.Equals(text, StringComparison.OrdinalIgnoreCase))
            await tg.SendMessageAsync(chatId, r.Text, replyTo);
    }
    catch (Exception ex)
    {
        // Stable, PII-free error line — never the message text or full upstream body.
        Console.Error.WriteLine($"handle error: {ex.GetType().Name}");
    }
}

static string Get(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

if (cfg.PublicUrl is { Length: > 0 } url)
{
    // ── Cloud mode: webhook. Telegram POSTs updates to us; Cloud Run wakes on demand. ──
    var builder = WebApplication.CreateBuilder(args);
    builder.Logging.SetMinimumLevel(LogLevel.Warning);
    var app = builder.Build();

    app.MapGet("/", () => "copper-bot ok");
    app.MapPost("/telegram/webhook", async (HttpRequest req) =>
    {
        // C1: only Telegram knows the secret token it echoes in this header. Reject anyone else
        // BEFORE doing any work, so a leaked URL can't drive the bot or burn model spend.
        var presented = req.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
        if (!string.Equals(presented, cfg.WebhookSecret, StringComparison.Ordinal))
            return Results.Unauthorized();

        JsonElement update;
        try { update = await JsonSerializer.DeserializeAsync<JsonElement>(req.Body); }
        catch { return Results.Ok(); }                 // malformed body → ack so Telegram won't retry-storm

        await HandleUpdate(update);                    // await: Cloud Run throttles CPU after the response
        return Results.Ok();
    });

    // Start listening FIRST, then register the webhook off the request path — don't gate startup
    // on outbound Telegram calls (M2: avoids cold-start probe failures).
    _ = Task.Run(async () =>
    {
        try
        {
            var me = await tg.GetMeUsernameAsync();
            await tg.SetWebhookAsync($"{url.TrimEnd('/')}/telegram/webhook", cfg.WebhookSecret);
            Console.WriteLine($"copper-bot @{me} · webhook set · model {cfg.Model} · langs {string.Join("/", cfg.Langs)}");
        }
        catch (Exception ex) { Console.Error.WriteLine($"startup webhook error: {ex.GetType().Name}"); }
    });
    app.Run($"http://0.0.0.0:{cfg.Port}");
}
else
{
    // ── Local mode: long-poll. No public URL needed — for testing before deploying. ──
    var me = await tg.GetMeUsernameAsync();
    await tg.DeleteWebhookAsync();
    Console.WriteLine($"copper-bot @{me} · long-poll (local) · model {cfg.Model} · langs {string.Join("/", cfg.Langs)}");
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
            Console.Error.WriteLine($"poll error: {ex.GetType().Name}");
            await Task.Delay(2000);
        }
    }
}

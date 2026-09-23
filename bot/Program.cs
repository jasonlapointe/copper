using System.Text.Json;
using CopperBot;

var cfg = Config.FromEnvironment();
var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
var tg = new Telegram(cfg.TelegramToken, http);
var people = new People(cfg.GcpProject);
var groups = new Groups(cfg.GcpProject);
var brain = new Brain(cfg, http);

// Core: translate one incoming message and reply in-thread. Never throws — logs and moves on.
async Task HandleUpdate(JsonElement update)
{
    try
    {
        if (update.ValueKind != JsonValueKind.Object) return;
        if (!update.TryGetProperty("message", out var m) || m.ValueKind != JsonValueKind.Object) return;
        if (!m.TryGetProperty("text", out var textEl) || textEl.ValueKind != JsonValueKind.String) return; // text only (MVP)
        var text = textEl.GetString()?.Trim() ?? "";
        if (text.Length == 0) return;

        if (!m.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var chatIdEl)) return;
        var chatId = chatIdEl.GetInt64();
        if (!cfg.ChatAllowed(chatId)) return;                             // allowlist bounds cost/abuse

        if (!m.TryGetProperty("from", out var from) || from.ValueKind != JsonValueKind.Object) return;
        if (from.TryGetProperty("is_bot", out var isBot) && isBot.ValueKind == JsonValueKind.True) return; // no bot↔bot loops
        if (!from.TryGetProperty("id", out var fromId)) return;
        var userId = fromId.GetInt64().ToString();
        var name = string.Join(" ", new[] { Get(from, "first_name"), Get(from, "last_name") }.Where(s => s.Length > 0));
        if (name.Length == 0) name = Get(from, "username");
        if (name.Length == 0) name = "unknown";
        var replyTo = m.TryGetProperty("message_id", out var mid) ? mid.GetInt32() : (int?)null;

        // ── Commands (not translated) ──
        if (text.StartsWith('/'))
        {
            var space = text.IndexOf(' ');
            var cmd = (space < 0 ? text : text[..space]).Split('@')[0].ToLowerInvariant(); // strip @botname
            var arg = space < 0 ? "" : text[(space + 1)..].Trim();

            if (cmd is "/language" or "/lang" or "/setlang")
            {
                if (arg.Length == 0)
                {
                    var cur = await groups.GetLanguageAsync(chatId, cfg.DefaultLanguage);
                    await tg.SendMessageAsync(chatId, $"This chat is translated into {cur}. Change it with:  /language <language>   (e.g. /language Russian)", replyTo);
                }
                else
                {
                    await groups.SetLanguageAsync(chatId, arg);
                    await tg.SendMessageAsync(chatId, $"✓ Group language set to {arg}. Messages will now be translated into {arg}.", replyTo);
                }
            }
            else if (cmd is "/start" or "/help")
            {
                var cur = await groups.GetLanguageAsync(chatId, cfg.DefaultLanguage);
                await tg.SendMessageAsync(chatId, $"I translate every message in this chat into its group language ({cur}), so everyone can follow along in one language. Change it with /language <language>.", replyTo);
            }
            return; // other commands: ignore
        }

        // ── Normal message → render into the chat's group language ──
        var target = await groups.GetLanguageAsync(chatId, cfg.DefaultLanguage);
        var r = await brain.TranslateAsync(text, name, await people.ContextBlockAsync(chatId), target);

        // Auto-seed the sender's detected language on first contact in this chat (decision #40).
        if (r.DetectedLang.Length > 0)
            await people.SeedAsync(chatId, userId, name, r.DetectedLang);

        // Post only when the message wasn't already in the group language (no echo).
        if (!r.AlreadyInTarget && r.Text.Length > 0)
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
            Console.WriteLine($"copper-bot @{me} · webhook set · model {cfg.Model} · default lang {cfg.DefaultLanguage}");
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
    Console.WriteLine($"copper-bot @{me} · long-poll (local) · model {cfg.Model} · default lang {cfg.DefaultLanguage}");
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

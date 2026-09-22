using System.Net.Http.Json;
using System.Text.Json;

namespace CopperBot;

/// <summary>Thin Telegram Bot API client over HTTPS — no third-party library, no bans, no patches.</summary>
public sealed class Telegram(string token, HttpClient http)
{
    private readonly string _base = $"https://api.telegram.org/bot{token}";

    public async Task<string> GetMeUsernameAsync()
    {
        var r = await CallAsync("getMe", new { });
        return r.GetProperty("username").GetString() ?? "";
    }

    public async Task SendMessageAsync(long chatId, string text, int? replyToMessageId = null)
    {
        await CallAsync("sendMessage", new { chat_id = chatId, text, reply_to_message_id = replyToMessageId });
    }

    /// <summary>Point Telegram at our public webhook URL with a secret token it echoes back for auth.</summary>
    public async Task SetWebhookAsync(string url, string secretToken)
        => await CallAsync("setWebhook", new { url, secret_token = secretToken });

    public async Task DeleteWebhookAsync() => await CallAsync("deleteWebhook", new { });

    /// <summary>Long-poll for updates (local mode only). Returns the raw update array.</summary>
    public async Task<JsonElement> GetUpdatesAsync(long offset)
    {
        var r = await CallRawAsync("getUpdates", new { offset, timeout = 50 });
        return r.GetProperty("result");
    }

    private async Task<JsonElement> CallAsync(string method, object body)
        => (await CallRawAsync(method, body)).GetProperty("result");

    private async Task<JsonElement> CallRawAsync(string method, object body)
    {
        using var resp = await http.PostAsJsonAsync($"{_base}/{method}", body);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        if (!json.GetProperty("ok").GetBoolean())
            throw new InvalidOperationException($"Telegram {method}: {json.GetProperty("description").GetString()}");
        return json;
    }
}

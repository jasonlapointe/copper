namespace CopperBot;

/// <summary>All configuration comes from environment variables — the 12-factor way, so the same
/// image runs locally and in Cloud Run with no code change and no secrets baked in.</summary>
public sealed class Config
{
    public required string TelegramToken { get; init; }
    /// <summary>GCP project for Vertex AI + Firestore (Gemini runs on Vertex, personas persist in Firestore).</summary>
    public required string GcpProject { get; init; }
    public string Region { get; init; } = "global";
    public string Model { get; init; } = "gemini-2.5-flash";
    public string[] Langs { get; init; } = ["en", "ru"];
    public string? PublicUrl { get; init; }
    public int Port { get; init; } = 8080;

    /// <summary>Shared secret Telegram echoes back in the webhook header — proves the request is from Telegram.</summary>
    public string WebhookSecret { get; init; } = "";
    /// <summary>If non-empty, only these chat ids are served (bounds cost/abuse). Empty = all chats.</summary>
    public long[] AllowedChats { get; init; } = [];

    public bool ChatAllowed(long id) => AllowedChats.Length == 0 || AllowedChats.Contains(id);

    public static Config FromEnvironment()
    {
        string require(string k) => Environment.GetEnvironmentVariable(k)
            ?? throw new InvalidOperationException($"Missing required env var {k}");
        string? opt(string k) => Environment.GetEnvironmentVariable(k);

        var publicUrl = opt("PUBLIC_URL");
        var webhookSecret = opt("WEBHOOK_SECRET") ?? "";
        if (!string.IsNullOrEmpty(publicUrl) && webhookSecret.Length == 0)
            throw new InvalidOperationException("WEBHOOK_SECRET is required in webhook mode (PUBLIC_URL set).");

        return new Config
        {
            TelegramToken = require("TELEGRAM_BOT_TOKEN"),
            GcpProject = require("GCP_PROJECT"),
            Region = opt("COPPER_REGION") ?? "global",
            Model = opt("COPPER_MODEL") ?? "gemini-2.5-flash",
            Langs = (opt("COPPER_LANGS") ?? "en,ru").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            PublicUrl = publicUrl,
            Port = int.TryParse(opt("PORT"), out var p) ? p : 8080,
            WebhookSecret = webhookSecret,
            AllowedChats = (opt("COPPER_ALLOWED_CHATS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => long.TryParse(s, out var v) ? v : 0).Where(v => v != 0).ToArray(),
        };
    }
}

namespace CopperBot;

/// <summary>All configuration comes from environment variables — the 12-factor way, so the same
/// image runs locally and in Cloud Run with no code change and no secrets baked in.</summary>
public sealed class Config
{
    public required string TelegramToken { get; init; }
    /// <summary>GCP project + region for Vertex AI (Claude runs on Vertex — GCP-native auth, no API key).</summary>
    public required string GcpProject { get; init; }
    public string Region { get; init; } = "us-east5";
    /// <summary>Vertex Claude model id. Cheap default; override with COPPER_MODEL.</summary>
    public string Model { get; init; } = "claude-3-5-haiku@20241022";
    /// <summary>The two (or more) languages this bot bridges, ISO codes. A message is translated into
    /// whichever configured language it is NOT. Default English/Russian.</summary>
    public string[] Langs { get; init; } = ["en", "ru"];
    /// <summary>Public HTTPS base URL (Cloud Run gives you one). Set → webhook mode. Unset → long-poll
    /// (handy for local testing with no public URL).</summary>
    public string? PublicUrl { get; init; }
    /// <summary>Where the people network lives. Local disk by default; a mounted volume or bucket-FUSE in cloud.</summary>
    public string DataDir { get; init; } = "data";
    public int Port { get; init; } = 8080;

    public static Config FromEnvironment()
    {
        string require(string k) => Environment.GetEnvironmentVariable(k)
            ?? throw new InvalidOperationException($"Missing required env var {k}");
        string? opt(string k) => Environment.GetEnvironmentVariable(k);

        return new Config
        {
            TelegramToken = require("TELEGRAM_BOT_TOKEN"),
            GcpProject = require("GCP_PROJECT"),
            Region = opt("COPPER_REGION") ?? "us-east5",
            Model = opt("COPPER_MODEL") ?? "claude-3-5-haiku@20241022",
            Langs = (opt("COPPER_LANGS") ?? "en,ru").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            PublicUrl = opt("PUBLIC_URL"),
            DataDir = opt("DATA_DIR") ?? "data",
            Port = int.TryParse(opt("PORT"), out var p) ? p : 8080,
        };
    }
}

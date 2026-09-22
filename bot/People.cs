using System.Collections.Concurrent;

namespace CopperBot;

/// <summary>
/// The people network, cloud edition: one markdown file per person under DataDir/people, keyed by
/// their Telegram user id. Auto-seeded on first contact (decision #40) — an unknown person who
/// speaks gets a file with their detected language, so the bridge learns people as they arrive
/// with zero manual setup.
/// </summary>
public sealed class People(string dataDir)
{
    private readonly string _dir = Path.Combine(dataDir, "people");
    private readonly ConcurrentDictionary<string, string> _lang = new(); // userId -> ISO lang

    public bool Knows(string userId) => _lang.ContainsKey(userId) || File.Exists(PathFor(userId));

    public string? LanguageOf(string userId)
    {
        if (_lang.TryGetValue(userId, out var l)) return l;
        var path = PathFor(userId);
        if (!File.Exists(path)) return null;
        foreach (var line in File.ReadLines(path))
            if (line.StartsWith("language:", StringComparison.OrdinalIgnoreCase))
            {
                var v = line["language:".Length..].Trim();
                _lang[userId] = v;
                return v;
            }
        return null;
    }

    /// <summary>Create a person's file on first contact with their detected language. Idempotent.</summary>
    public void Seed(string userId, string displayName, string lang)
    {
        _lang[userId] = lang;
        Directory.CreateDirectory(_dir);
        var path = PathFor(userId);
        if (File.Exists(path)) return;
        File.WriteAllText(path,
            $"# {displayName}\n" +
            $"language: {lang}\n" +
            $"telegram_id: {userId}\n" +
            $"first_seen: {DateTime.UtcNow:yyyy-MM-dd}\n\n" +
            $"## Grounding\n**As of {DateTime.UtcNow:yyyy-MM-dd}.** Auto-added on first contact; writes in {lang}. " +
            $"Copper refines this as it learns more about them.\n\n## Learned\n");
    }

    /// <summary>Everything known about the people in a chat — fed to the model as translation context.</summary>
    public string ContextBlock()
    {
        if (!Directory.Exists(_dir)) return "(no people known yet)";
        var sb = new System.Text.StringBuilder();
        foreach (var f in Directory.EnumerateFiles(_dir, "*.md"))
            sb.AppendLine(File.ReadAllText(f).Trim()).AppendLine();
        return sb.Length > 0 ? sb.ToString() : "(no people known yet)";
    }

    private string PathFor(string userId) => Path.Combine(_dir, Sanitize(userId) + ".md");
    private static string Sanitize(string s) => new(s.Where(char.IsLetterOrDigit).ToArray());
}

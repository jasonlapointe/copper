using System.Text.Json;

namespace Copper;

/// <summary>Appends one JSON line per conversation turn to logs/turns.jsonl — usage monitoring.</summary>
public sealed class TurnLogger(string baseDir)
{
    public string Path { get; } = System.IO.Path.Combine(baseDir, "logs", "turns.jsonl");

    public void Log(object entry)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.AppendAllText(Path, JsonSerializer.Serialize(entry) + Environment.NewLine);
    }

    /// <summary>How many logged events (translates, drafts, sends) happened since a moment — the deterministic "how much has life moved" signal.</summary>
    public int CountSince(DateTime sinceUtc)
    {
        if (!File.Exists(Path)) return 0;
        var count = 0;
        foreach (var line in File.ReadLines(Path))
        {
            var i = line.IndexOf("\"ts\":\"", StringComparison.Ordinal);
            if (i < 0) continue;
            var end = line.IndexOf('"', i + 6);
            if (end < 0) continue;
            if (DateTime.TryParse(line[(i + 6)..end], null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var d) && d >= sinceUtc)
                count++;
        }
        return count;
    }
}

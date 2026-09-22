using System.Collections.Concurrent;
using System.Text.Json;

namespace Copper;

public sealed record StoredTranslation(string English, string? Note);
public sealed record SentRecord(string English, string Back);

/// <summary>
/// Durable per-contact translation cache: one JSONL file, keyed by WhatsApp message id.
/// A message is translated once, ever — loads after that are instant and free.
/// </summary>
public sealed class TranslationStore
{
    private readonly string _path;
    private readonly string _sentPath;
    private readonly ConcurrentDictionary<string, StoredTranslation> _map = new();
    // Sent messages keyed by the outgoing (Russian) text, so ME bubbles echoed back from
    // WhatsApp can be shown as Jason's original English with the Russian available on tap.
    private readonly ConcurrentDictionary<string, SentRecord> _sent = new();

    public TranslationStore(string baseDir, string slug)
    {
        _path = Path.Combine(baseDir, "store", slug + ".jsonl");
        _sentPath = Path.Combine(baseDir, "store", slug + ".sent.jsonl");
        if (File.Exists(_path))
            foreach (var line in File.ReadLines(_path))
                try
                {
                    var e = JsonDocument.Parse(line).RootElement;
                    if (e.GetProperty("id").GetString() is not { Length: > 0 } id) continue;
                    _map[id] = new StoredTranslation(
                        e.GetProperty("english").GetString() ?? "",
                        e.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null);
                }
                catch { /* skip corrupt line */ }

        if (File.Exists(_sentPath))
            foreach (var line in File.ReadLines(_sentPath))
                try
                {
                    var e = JsonDocument.Parse(line).RootElement;
                    var ru = e.GetProperty("russian").GetString() ?? "";
                    if (ru.Length > 0)
                        _sent[ru] = new SentRecord(
                            e.GetProperty("english").GetString() ?? "",
                            e.TryGetProperty("back", out var b) ? b.GetString() ?? "" : "");
                }
                catch { /* skip corrupt line */ }
    }

    public bool TryGet(string id, out StoredTranslation translation) => _map.TryGetValue(id, out translation!);

    public void Save(string id, string body, StoredTranslation translation)
    {
        _map[id] = translation;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.AppendAllText(_path,
            JsonSerializer.Serialize(new { id, body, english = translation.English, note = translation.Note, ts = DateTime.UtcNow })
            + Environment.NewLine);
    }

    public bool TryGetSent(string russian, out SentRecord sent) => _sent.TryGetValue(russian, out sent!);

    public void SaveSent(string russian, string english, string back)
    {
        _sent[russian] = new SentRecord(english, back);
        Directory.CreateDirectory(Path.GetDirectoryName(_sentPath)!);
        File.AppendAllText(_sentPath,
            JsonSerializer.Serialize(new { russian, english, back, ts = DateTime.UtcNow }) + Environment.NewLine);
    }
}

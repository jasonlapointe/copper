using System.Collections.Concurrent;
using System.Text;
using Google.Cloud.Firestore;

namespace CopperBot;

/// <summary>
/// The people network — the product's core value — persisted in Firestore so it survives Cloud
/// Run scale-to-zero (a local file would vanish). One document per person, keyed by Telegram user
/// id; each carries their name, detected language, the chats they appear in, and any learned notes.
/// Auto-seeded on first contact (decision #40). Context is scoped to the current chat's
/// participants, so no chat's personas leak into another chat's translation prompt.
/// </summary>
public sealed class People(string projectId)
{
    private readonly FirestoreDb _db = FirestoreDb.Create(projectId);
    private readonly ConcurrentDictionary<string, byte> _seenThisInstance = new();

    /// <summary>Create/refresh a person on first contact in a chat. Preserves learned notes; never throws.</summary>
    public async Task SeedAsync(long chatId, string userId, string name, string lang)
    {
        // One durable write per (chat, user) pairing per instance lifetime — cheap and idempotent.
        if (!_seenThisInstance.TryAdd(chatId + ":" + userId, 0)) return;

        var doc = _db.Collection("people").Document(userId);
        var snap = await doc.GetSnapshotAsync();
        var data = new Dictionary<string, object>
        {
            ["name"] = name,
            ["language"] = lang,
            ["chats"] = FieldValue.ArrayUnion(chatId.ToString()),
            ["lastSeen"] = FieldValue.ServerTimestamp,
        };
        if (!snap.Exists) data["firstSeen"] = FieldValue.ServerTimestamp;
        await doc.SetAsync(data, SetOptions.MergeAll);
    }

    /// <summary>Personas of the people in THIS chat, as translation context. Empty-safe.</summary>
    public async Task<string> ContextBlockAsync(long chatId)
    {
        var snap = await _db.Collection("people")
            .WhereArrayContains("chats", chatId.ToString())
            .GetSnapshotAsync();
        if (snap.Count == 0) return "(no people known in this chat yet)";

        var sb = new StringBuilder();
        foreach (var d in snap.Documents)
        {
            var name = d.TryGetValue<string>("name", out var n) ? n : "someone";
            var lang = d.TryGetValue<string>("language", out var l) ? l : "?";
            sb.Append("- ").Append(name).Append(" (writes ").Append(lang).Append(')');
            if (d.TryGetValue<string>("notes", out var notes) && notes.Length > 0)
                sb.Append(": ").Append(notes);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}

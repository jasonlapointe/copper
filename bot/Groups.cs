using Google.Cloud.Firestore;

namespace CopperBot;

/// <summary>
/// Per-chat settings, persisted in Firestore. Right now that's just the group language —
/// the single language a chat is translated into — which any member can change with /language.
/// </summary>
public sealed class Groups(string projectId)
{
    private readonly FirestoreDb _db = FirestoreDb.Create(projectId);

    /// <summary>The chat's group language, or <paramref name="fallback"/> if it was never set.</summary>
    public async Task<string> GetLanguageAsync(long chatId, string fallback)
    {
        var snap = await _db.Collection("groups").Document(chatId.ToString()).GetSnapshotAsync();
        return snap.Exists && snap.TryGetValue<string>("language", out var l) && l.Length > 0 ? l : fallback;
    }

    public async Task SetLanguageAsync(long chatId, string language)
    {
        await _db.Collection("groups").Document(chatId.ToString()).SetAsync(
            new Dictionary<string, object> { ["language"] = language, ["updated"] = FieldValue.ServerTimestamp },
            SetOptions.MergeAll);
    }
}

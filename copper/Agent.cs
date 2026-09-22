using System.Text;
using System.Text.Json;

namespace Copper;

public sealed record SentMessage(string English, string Russian, string Back);
public sealed record TranslationOut(int N, string English, string? Note);
public sealed record TurnOutcome(SentMessage? Sent, string? Ack);

/// <summary>
/// Deterministic pipeline, LLM at the edges only.
/// Copper's code owns polling, history, the live feed, sending, caching, and the people network.
/// The LLM is called only as stateless, single-shot edges, with context assembled by code:
///   TranslateAsync     — incoming messages  → contextually aware text in the user's language
///   ProcessAsync       — the user's message → sent in the contact's language (or filed as a note)
///   RenderCaptionAsync — a photo caption    → the contact's language
///   RefreshStaleGroundingsAsync — rewrites a person's "who they are now" grounding when stale
/// </summary>
public sealed class Agent(AppConfig config, WaService wa, ClaudeCli brain, TurnLogger log, TranslationStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1); // serialize LLM calls
    public event Action<string>? OnStatus;

    /* ── the two LLM edges ─────────────────────────────────────────── */

    public IReadOnlyList<TranslationOut> Cached(IReadOnlyList<(int N, string Id)> items)
    {
        List<TranslationOut> hits = [];
        foreach (var (n, id) in items)
            if (id.Length > 0 && store.TryGet(id, out var t))
                hits.Add(new TranslationOut(n, t.English, t.Note));
        return hits;
    }

    public async Task<IReadOnlyList<TranslationOut>> TranslateAsync(IReadOnlyList<(int N, string Id, string Body, string? Media)> messages)
    {
        var numbered = new StringBuilder();
        var hasImages = false;
        foreach (var (n, _, body, media) in messages)
        {
            if (media is not null)
            {
                hasImages = true;
                numbered.AppendLine($"{n}. [image file: media/{media}]{(body.StartsWith('[') ? "" : $" (sender's caption: {body})")}");
            }
            else
            {
                numbered.AppendLine($"{n}. {body}");
            }
        }
        var imageInstruction = hasImages
            ? "\nSome entries are image files under media/ — READ each image file to see it. For an image, \"english\" is a natural contextual caption for Jason: who/where/what it shows and why it matters (e.g. \"Marat measuring the dombra body — about 30 cm across\"). Record durable visual facts (places he went, people present, objects that matter) in \"learned\"."
            : "";

        var context = await ContextAsync();
        var prompt = $$"""
            {{context}}

            TASK: Translate these WhatsApp messages FROM {{config.Contact.Name}} for Jason. Contextually aware,
            natural English — how {{config.Contact.Name}} would have said it if English were his own language.
            Use the people network and recent chat for context. Dignity rule: he must read as the fluent,
            intelligent person he is; never word-for-word. Add "note" only when something genuinely doesn't
            carry (idiom, kinship term, cultural reference) — one short line.{{imageInstruction}}

            {{numbered}}
            Answer ONLY this JSON, no fences: {"translations":[{"n":1,"english":"...","note":null}],"learned":[{"person":"marat","category":"life","note":"durable fact, absolute dates"}]}
            ("learned" is optional — only durable facts worth remembering.)
            """;

        var root = await CallAsync(prompt, "translate");
        List<TranslationOut> outp = [];
        var idByN = messages.ToDictionary(m => m.N, m => (m.Id, m.Body));
        if (root.TryGetProperty("translations", out var tr) && tr.ValueKind == JsonValueKind.Array)
            foreach (var item in tr.EnumerateArray())
            {
                var n = item.TryGetProperty("n", out var tn) ? tn.GetInt32() : 0;
                var english = item.TryGetProperty("english", out var te) ? te.GetString() ?? "" : "";
                var note = item.TryGetProperty("note", out var tno) && tno.ValueKind == JsonValueKind.String ? tno.GetString() : null;
                outp.Add(new TranslationOut(n, english, note));
                // Persist so this message is never re-translated (id-keyed, survives restarts).
                if (english.Length > 0 && idByN.TryGetValue(n, out var m) && m.Id.Length > 0)
                    store.Save(m.Id, m.Body, new StoredTranslation(english, note));
            }
        return outp;
    }

    public async Task<TurnOutcome> ProcessAsync(string input)
    {
        var context = await ContextAsync();
        var prompt = $$"""
            {{context}}

            TASK: Jason typed this into Copper's message box for {{config.Contact.Name}}: "{{input}}"

            CRITICAL: You are a translator, not a gatekeeper. NEVER refuse, NEVER pause, NEVER ask a
            clarifying question, NEVER return prose. If a name or word is unfamiliar (e.g. a person not in
            the network), transliterate it faithfully into the target language and proceed — an unknown name
            is never a reason to block a message. You ALWAYS return the JSON below and nothing else.

            Decide what it is:
            - A MESSAGE for {{config.Contact.Name}} — the DEFAULT for anything he'd plausibly say to him.
              Render it in the contact's working language (see profile — Russian with Marat, matching his
              register; a Kazakh greeting flourish is fine). Jason's voice: concise, direct, precise, warm but
              economical — say what he said, nothing more, natural to a local ear. Dignity rule in force. This
              WILL BE SENT immediately (Jason pressed enter — that is his approval), so render faithfully.
              Fill "text" and "back_translation"; "ack" stays null.
            - SEEDING/GUIDANCE for YOU, not a message to send — facts about the people ("btw Marat runs a
              construction company", "Sabira's birthday is in March"), background, corrections, style guidance.
              Only when clearly not meant for {{config.Contact.Name}}: signals like "btw", "fyi", "note that",
              "remember", "for context", third-person facts with no message intent. Record in "learned" (right
              person, absolute dates), set "ack" to one short confirming line; "text" stays null.

            Answer ONLY this JSON, no fences: {"text":"outgoing message in his language","back_translation":"faithful English of what you're sending","ack":null,"learned":[]}
            """;

        var root = await CallAsync(prompt, "process");
        if (root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() is { Length: > 0 } outgoing)
        {
            var back = root.TryGetProperty("back_translation", out var bt) ? bt.GetString() ?? "" : "";
            // Seamless: Jason's enter IS the send. Transmit now; the back-translation is his after-the-fact record.
            OnStatus?.Invoke("sending…");
            await wa.SendAsync(outgoing);
            store.SaveSent(outgoing, input, back); // so the echoed-back WhatsApp bubble shows English
            log.Log(new { ts = DateTime.UtcNow.ToString("o"), kind = "send", input, russian = outgoing });
            return new TurnOutcome(new SentMessage(input, outgoing, back), null);
        }
        var ack = root.TryGetProperty("ack", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
        return new TurnOutcome(null, ack ?? "Noted.");
    }

    /// <summary>
    /// Deterministic staleness check; LLM rewrites the prose. A grounding statement is the dated
    /// "who this person is right now" snapshot at the top of their file — days/weeks foregrounded,
    /// months as background, stale details dropped (a person is not their note history).
    /// </summary>
    public async Task RefreshStaleGroundingsAsync(TimeSpan maxAge, int minEventsToTweak = 10)
    {
        foreach (var (slug, contents) in config.LoadPeople())
        {
            var asOf = config.GroundingAsOf(slug);
            // Two deterministic triggers: age (people drift with time) and activity
            // (the last X turns may have tweaked who they are — refresh the persona from them).
            var stale = asOf is null || DateTime.Now - asOf.Value >= maxAge;
            var active = asOf is not null
                && (slug == config.ContactSlug || slug == "jason")
                && log.CountSince(asOf.Value.ToUniversalTime()) >= minEventsToTweak;
            if (!stale && !active) continue;
            try
            {
                var isContact = slug == config.ContactSlug;
                string transcript = "";
                if (isContact)
                {
                    try { transcript = "## Recent chat (oldest first, UTC)\n" + await wa.ReadAsync(15); } catch { }
                }
                var prompt = $$"""
                    You maintain the "grounding statement" for {{slug}} in Copper, the interpreter between Jason
                    (English) and his family in Kazakhstan. The grounding statement is the dated snapshot of who
                    this person is RIGHT NOW, used to anchor translation: like a person, it must grow — last week's
                    reality foregrounded, last months as background, and details that no longer matter dropped.

                    ## Their file today (existing grounding, facts, dated learned notes)
                    {{contents}}
                    {{transcript}}

                    Write the new grounding statement: 3–6 sentences, present tense, concrete. Cover: relationship
                    to Jason, current life state (weight recent dated notes over old ones), current communication
                    register/dynamics, and anything an interpreter must know this week. No preamble.
                    Answer ONLY this JSON, no fences: {"grounding":"..."}
                    """;
                var root = await CallAsync(prompt, "grounding");
                if (root.TryGetProperty("grounding", out var g) && g.ValueKind == JsonValueKind.String && g.GetString() is { Length: > 0 } statement)
                {
                    config.ReplaceGrounding(slug, statement);
                    OnStatus?.Invoke($"grounding refreshed: {slug}");
                }
            }
            catch
            {
                // Grounding refresh is best-effort; stale is better than broken.
            }
        }
    }

    /// <summary>Render a photo caption into the contact's language (no send — the caller sends the media).</summary>
    public async Task<(string Russian, string Back)> RenderCaptionAsync(string english)
    {
        var context = await ContextAsync();
        var prompt = $$"""
            {{context}}

            TASK: Jason is sending {{config.Contact.Name}} a PHOTO with this caption: "{{english}}"
            Render just the caption in the contact's working language, in Jason's voice (concise, warm,
            natural to a local ear; dignity rule in force).
            Answer ONLY this JSON, no fences: {"text":"caption in his language","back_translation":"faithful English"}
            """;
        var root = await CallAsync(prompt, "caption");
        return (
            root.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "",
            root.TryGetProperty("back_translation", out var b) ? b.GetString() ?? "" : "");
    }

    /// <summary>Tiny auth/health probe of the brain; throws with the CLI's error when it can't answer.</summary>
    public Task PingAsync() => brain.RunAsync("Answer with exactly this JSON and nothing else: {\"ok\":true}");

    /* ── plumbing ──────────────────────────────────────────────────── */

    /// <summary>Context assembled deterministically by code: people network + recent transcript.</summary>
    private async Task<string> ContextAsync()
    {
        string transcript;
        try { transcript = await wa.ReadAsync(15); }
        catch (Exception ex) { transcript = "(recent history unavailable: " + ex.Message + ")"; }

        var people = new StringBuilder();
        foreach (var (slug, contents) in config.LoadPeople())
            people.AppendLine($"### {slug}\n{contents.Trim()}\n");

        return $"""
            You are Copper, the interpreter between Jason (English) and {config.Contact.Name} on WhatsApp.
            Values, ranked: harmony (closer people, not translated text), dignity (no one ever sounds
            "translated"), fidelity (meaning carried whole, nothing invented), knowing over rendering.
            Kazakhstan is UTC+5, Jason is US Eastern; transcript timestamps are UTC — flag ambiguous times.

            ## People network — who these people ARE (stable; anchors everything)
            {people}
            ## Recent chat, oldest first — what is HAPPENING right now (weight it, don't be ruled by it)
            {transcript}

            ## Interpreting under tension
            The recent transcript tells you the moment; the people files tell you the relationship. When the two
            conflict — a fight, a misunderstanding, a cold patch — you are the one calm head in the room:
            - Intensity in = intensity out, EXACTLY. Never sharpen, never soften what was clearly meant.
              Muting someone's anger is as unfaithful as inflaming it.
            - Ambiguity resolves to the LESS inflammatory reading, with a note saying the harsher reading exists.
              A heated hour must not redefine a warm relationship; assume the person in the people file wrote it.
            - Never editorialize, never take sides, never inject apology or blame that isn't in the words.
            - You escalate nothing silently: if something will land harsher (or softer) in the other language
              than its author likely intends, say so in the note — the human decides with eyes open.
            """;
    }

    private async Task<JsonElement> CallAsync(string prompt, string kind)
    {
        await _gate.WaitAsync();
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            OnStatus?.Invoke(kind == "draft" ? "composing…" : "interpreting…");
            var (result, raw) = await brain.RunAsync(prompt);
            var root = ParseJson(result);

            var learned = 0;
            if (root.TryGetProperty("learned", out var ln) && ln.ValueKind == JsonValueKind.Array)
                foreach (var item in ln.EnumerateArray())
                {
                    var note = item.TryGetProperty("note", out var n) ? n.GetString() ?? "" : "";
                    if (note.Length == 0) continue;
                    var person = item.TryGetProperty("person", out var p) ? p.GetString() ?? config.ContactSlug : config.ContactSlug;
                    var category = item.TryGetProperty("category", out var c) ? c.GetString() ?? "note" : "note";
                    config.AppendPersonNote(person, category, note);
                    learned++;
                    OnStatus?.Invoke($"noted about {person}: {note}");
                }

            long tokIn = 0, tokOut = 0; double cost = 0;
            if (raw.TryGetProperty("usage", out var usage))
            {
                tokIn = usage.TryGetProperty("input_tokens", out var it) ? it.GetInt64() : 0;
                tokOut = usage.TryGetProperty("output_tokens", out var ot) ? ot.GetInt64() : 0;
            }
            if (raw.TryGetProperty("total_cost_usd", out var tc) && tc.ValueKind == JsonValueKind.Number) cost = tc.GetDouble();
            log.Log(new
            {
                ts = DateTime.UtcNow.ToString("o"),
                kind,
                learned,
                input_tokens = tokIn,
                output_tokens = tokOut,
                cost_usd = cost,
                duration_ms = started.ElapsedMilliseconds,
            });
            return root;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static JsonElement ParseJson(string result)
    {
        var text = result.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                text = text[(firstNewline + 1)..lastFence].Trim();
        }
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidOperationException("Model did not return JSON: " + text[..Math.Min(120, text.Length)]);
        return JsonDocument.Parse(text[start..(end + 1)]).RootElement;
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;

namespace CopperBot;

/// <summary><see cref="AlreadyInTarget"/> true means the message was already in the group language —
/// nothing should be posted. Otherwise <see cref="Text"/> is the translation into it.</summary>
public sealed record Rendered(string DetectedLang, bool AlreadyInTarget, string Text);

/// <summary>
/// The one LLM edge: render a message into the chat's GROUP LANGUAGE, contextually and with dignity.
/// Detects the sender's language on the fly (any language), and skips messages already in the group
/// language so people never see a pointless echo. Runs on Gemini via GCP Vertex AI (service-account
/// auth, no API key).
/// </summary>
public sealed class Brain(Config cfg, HttpClient http)
{
    private readonly string _endpoint = BuildEndpoint(cfg);
    private GoogleCredential? _cred;

    private static string BuildEndpoint(Config c)
    {
        var host = c.Region == "global" ? "aiplatform.googleapis.com" : $"{c.Region}-aiplatform.googleapis.com";
        return $"https://{host}/v1/projects/{c.GcpProject}/locations/{c.Region}" +
               $"/publishers/google/models/{c.Model}:generateContent";
    }

    /// <summary>Translate <paramref name="message"/> into <paramref name="targetLanguage"/> using people context.</summary>
    public async Task<Rendered> TranslateAsync(string message, string sender, string peopleContext, string targetLanguage)
    {
        var prompt = $$"""
            You are Copper, an interpreter that makes a cross-language chat feel like everyone speaks the
            same language. Values: dignity (no one ever sounds "translated"), fidelity (carry meaning whole,
            invent nothing, keep names as names), warmth to match the speaker.

            The chat's group language is: {{targetLanguage}}.

            ## People in the conversation (use for tone, register, and who names refer to)
            {{peopleContext}}

            ## Message just sent by {{sender}}
            {{message}}

            First detect the message's own language. If it is ALREADY in {{targetLanguage}}, set
            "same" true and "text" to "" (nothing needs translating). Otherwise set "same" false and
            "text" to a natural, fluent rendering into {{targetLanguage}} — as {{sender}} would say it
            if {{targetLanguage}} were their own language; never word-for-word.

            Answer ONLY this JSON, no prose, no code fences:
            {"detected":"<language name>","same":true|false,"text":"<translation or empty>"}
            """;

        var token = await AccessTokenAsync();
        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        req.Headers.Add("Authorization", $"Bearer {token}");
        req.Content = JsonContent.Create(new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
            generationConfig = new { maxOutputTokens = 1024, temperature = 0.2, thinkingConfig = new { thinkingBudget = 0 } },
        });

        using var resp = await http.SendAsync(req);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Vertex/Gemini {(int)resp.StatusCode}");

        var text = body.GetProperty("candidates")[0].GetProperty("content")
            .GetProperty("parts")[0].GetProperty("text").GetString() ?? "";

        var json = ExtractJson(text);
        return new Rendered(
            json.TryGetProperty("detected", out var d) ? d.GetString() ?? "" : "",
            json.TryGetProperty("same", out var s) && s.ValueKind == JsonValueKind.True,
            json.TryGetProperty("text", out var x) ? x.GetString() ?? "" : "");
    }

    private async Task<string> AccessTokenAsync()
    {
        _cred ??= (await GoogleCredential.GetApplicationDefaultAsync())
            .CreateScoped("https://www.googleapis.com/auth/cloud-platform");
        return await _cred.UnderlyingCredential.GetAccessTokenForRequestAsync();
    }

    private static JsonElement ExtractJson(string s)
    {
        var a = s.IndexOf('{');
        var b = s.LastIndexOf('}');
        if (a < 0 || b <= a) throw new InvalidOperationException("Model did not return JSON");
        return JsonDocument.Parse(s[a..(b + 1)]).RootElement;
    }
}

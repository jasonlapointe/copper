using System.Net.Http.Json;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;

namespace CopperBot;

public sealed record Rendered(string DetectedLang, string TargetLang, string Text);

/// <summary>
/// The one LLM edge: translate a message into the group's other language, contextually and with
/// dignity. Calls Claude on GCP Vertex AI — authenticated by the service account (metadata server
/// on Cloud Run, ADC locally), so there is NO API key to manage. Cheap model by default (Haiku).
/// </summary>
public sealed class Brain(Config cfg, HttpClient http)
{
    private readonly string _endpoint =
        $"https://{cfg.Region}-aiplatform.googleapis.com/v1/projects/{cfg.GcpProject}" +
        $"/locations/{cfg.Region}/publishers/anthropic/models/{cfg.Model}:rawPredict";
    private GoogleCredential? _cred;

    public async Task<Rendered> TranslateAsync(string message, string sender, string peopleContext)
    {
        var langs = string.Join(", ", cfg.Langs);
        var prompt = $$"""
            You are Copper, an interpreter that makes a cross-language group feel like everyone speaks
            the same language. Values: dignity (no one ever sounds "translated"), fidelity (carry meaning
            whole, invent nothing), warmth to match the speaker. This group's languages: {{langs}}.

            ## People in the conversation
            {{peopleContext}}

            ## Message just sent by {{sender}}
            {{message}}

            Detect the message's language (ISO code). Translate it into the OTHER group language so the
            rest can read it — natural, fluent, as that person would say it if it were their own language;
            never word-for-word. If it's already understandable to everyone or is just an emoji/link,
            you may echo it. Always answer ONLY this JSON, no prose, no fences:
            {"detected":"<iso>","target":"<iso>","text":"<translation>"}
            """;

        var token = await AccessTokenAsync();
        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        req.Headers.Add("Authorization", $"Bearer {token}");
        req.Content = JsonContent.Create(new
        {
            anthropic_version = "vertex-2023-10-16", // Vertex Claude: version in body, model in the URL
            max_tokens = 1024,
            messages = new[] { new { role = "user", content = prompt } },
        });

        using var resp = await http.SendAsync(req);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Vertex {(int)resp.StatusCode}: {body}");

        var text = "";
        foreach (var block in body.GetProperty("content").EnumerateArray())
            if (block.GetProperty("type").GetString() == "text") { text = block.GetProperty("text").GetString() ?? ""; break; }

        var json = ExtractJson(text);
        return new Rendered(
            json.TryGetProperty("detected", out var d) ? d.GetString() ?? "" : "",
            json.TryGetProperty("target", out var t) ? t.GetString() ?? "" : "",
            json.TryGetProperty("text", out var x) ? x.GetString() ?? "" : "");
    }

    /// <summary>Bearer token from Application Default Credentials — auto-refreshed by the library.</summary>
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
        if (a < 0 || b <= a) throw new InvalidOperationException("Model did not return JSON: " + s);
        return JsonDocument.Parse(s[a..(b + 1)]).RootElement;
    }
}

using System.Net.Http.Json;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;

namespace CopperBot;

public sealed record Rendered(string DetectedLang, string TargetLang, string Text);

/// <summary>
/// The one LLM edge: translate a message into the group's other language, contextually and with
/// dignity. Uses Gemini on GCP Vertex AI — Google's own model, so a new project has quota
/// immediately and it bills straight through GCP with no API key (service-account auth: metadata
/// server on Cloud Run, ADC locally). Thinking is disabled for fast, complete, cheap translations.
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

    public async Task<Rendered> TranslateAsync(string message, string sender, string peopleContext)
    {
        var langs = string.Join(", ", cfg.Langs);
        var prompt = $$"""
            You are Copper, an interpreter that makes a cross-language group feel like everyone speaks
            the same language. Values: dignity (no one ever sounds "translated"), fidelity (carry meaning
            whole, invent nothing — never guess at names, keep them as-is), warmth to match the speaker.
            This group's languages: {{langs}}.

            ## People in the conversation
            {{peopleContext}}

            ## Message just sent by {{sender}}
            {{message}}

            Detect the message's language (ISO code). Translate it into the OTHER group language so the
            rest can read it — natural, fluent, as that person would say it if it were their own language;
            never word-for-word. Names of people (see the people list) stay as names. If it's already
            understandable to everyone or is just an emoji/link, you may echo it. Answer ONLY this JSON,
            no prose, no code fences:
            {"detected":"<iso>","target":"<iso>","text":"<translation>"}
            """;

        var token = await AccessTokenAsync();
        using var req = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        req.Headers.Add("Authorization", $"Bearer {token}");
        req.Content = JsonContent.Create(new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
            generationConfig = new
            {
                maxOutputTokens = 1024,
                temperature = 0.2,
                thinkingConfig = new { thinkingBudget = 0 }, // translation needs no reasoning tokens
            },
        });

        using var resp = await http.SendAsync(req);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Vertex/Gemini {(int)resp.StatusCode}: {body}");

        var text = body.GetProperty("candidates")[0].GetProperty("content")
            .GetProperty("parts")[0].GetProperty("text").GetString() ?? "";

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

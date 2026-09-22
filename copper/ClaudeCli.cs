using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Copper;

/// <summary>
/// Stateless LLM call via `claude -p` headless (this machine's Claude Code login, no API key).
/// Deterministic pipeline: no sessions, no resume — full context is assembled per call by our code.
/// </summary>
public sealed class ClaudeCli(string workingDir)
{
    public async Task<(string Result, JsonElement Raw)> RunAsync(string prompt)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "claude",
            WorkingDirectory = workingDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        // Copper may be launched from a Claude Code terminal; headless -p in its own process is safe.
        psi.Environment.Remove("CLAUDECODE");
        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("json");

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start claude CLI.");
        await proc.StandardInput.WriteAsync(prompt);
        proc.StandardInput.Close();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await proc.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("claude CLI timed out.");
        }

        var stdout = await stdoutTask;
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"claude CLI failed (exit {proc.ExitCode}): {(await stderrTask).Trim()} {Truncate(stdout)}");

        var raw = JsonDocument.Parse(stdout).RootElement;
        if (raw.TryGetProperty("is_error", out var isErr) && isErr.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException("LLM error: " + Truncate(raw.TryGetProperty("result", out var er) ? er.GetString() ?? "" : ""));
        var result = raw.TryGetProperty("result", out var r) ? r.GetString() ?? "" : "";
        return (result, raw);
    }

    private static string Truncate(string s) => s.Length > 300 ? s[..300] + "…" : s;
}

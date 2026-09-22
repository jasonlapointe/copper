using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Copper;

/// <summary>
/// One persistent `node wa.js serve` process for the whole session: streams incoming
/// messages live and answers read/send/chats/status requests over JSON lines.
/// (Only one wa.js process can run at a time — Chromium locks the WhatsApp profile.)
/// </summary>
public sealed class WaService(string bridgeDir, string chatName) : IDisposable
{
    private Process? _proc;
    private int _nextId;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly TaskCompletionSource<string> _ready = new();

    /// <summary>Raised on the reader thread when the contact sends a message while Copper runs (id, body, media file or null).</summary>
    public event Action<string, string, string?>? OnLiveMessage;

    public async Task StartAsync(TimeSpan timeout)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = bridgeDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = new UTF8Encoding(false),
        };
        psi.ArgumentList.Add("wa.js");
        psi.ArgumentList.Add("serve");
        psi.ArgumentList.Add(chatName);

        _proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start node.");
        _ = Task.Run(ReadLoopAsync);
        _ = Task.Run(async () =>
        {
            var err = await _proc.StandardError.ReadToEndAsync();
            if (!_ready.Task.IsCompleted && err.Length > 0)
                _ready.TrySetException(new InvalidOperationException(err.Trim()));
        });

        using var cts = new CancellationTokenSource(timeout);
        await _ready.Task.WaitAsync(cts.Token);
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (_proc is { HasExited: false })
            {
                var line = await _proc.StandardOutput.ReadLineAsync();
                if (line is null) break;
                line = line.Trim();
                if (!line.StartsWith('{')) continue;

                JsonElement msg;
                try { msg = JsonDocument.Parse(line).RootElement; } catch { continue; }

                if (msg.TryGetProperty("event", out var ev))
                {
                    switch (ev.GetString())
                    {
                        case "ready":
                            _ready.TrySetResult(msg.GetProperty("chat").GetString() ?? chatName);
                            break;
                        case "message":
                            OnLiveMessage?.Invoke(
                                msg.TryGetProperty("id", out var mid) && mid.ValueKind == JsonValueKind.String ? mid.GetString() ?? "" : "",
                                msg.GetProperty("body").GetString() ?? "",
                                msg.TryGetProperty("media", out var med) && med.ValueKind == JsonValueKind.String ? med.GetString() : null);
                            break;
                        case "fatal":
                            var error = msg.GetProperty("error").GetString() ?? "bridge died";
                            _ready.TrySetException(new InvalidOperationException(error));
                            FailAllPending(error);
                            break;
                    }
                }
                else if (msg.TryGetProperty("id", out var id) &&
                         _pending.TryRemove(id.GetInt32(), out var tcs))
                {
                    tcs.TrySetResult(msg);
                }
            }
        }
        finally
        {
            FailAllPending("The WhatsApp bridge process exited.");
        }
    }

    private void FailAllPending(string reason)
    {
        foreach (var key in _pending.Keys)
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetException(new InvalidOperationException(reason));
    }

    private async Task<JsonElement> RequestAsync(object payload, int id)
    {
        var proc = _proc ?? throw new InvalidOperationException("Bridge not started.");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        await proc.StandardInput.WriteLineAsync(JsonSerializer.Serialize(payload));
        await proc.StandardInput.FlushAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var response = await tcs.Task.WaitAsync(cts.Token);
        if (response.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            return response.GetProperty("data");
        throw new InvalidOperationException(
            response.TryGetProperty("error", out var err) ? err.GetString() : "bridge error");
    }

    /// <summary>Raw history: array of { t (unix seconds), who (ME/THEM), body }.</summary>
    public async Task<JsonElement> ReadRawAsync(int limit)
    {
        var id = Interlocked.Increment(ref _nextId);
        return await RequestAsync(new { id, cmd = "read", limit }, id);
    }

    /// <summary>History formatted for the agent's context.</summary>
    public async Task<string> ReadAsync(int limit)
    {
        var data = await ReadRawAsync(limit);
        var sb = new StringBuilder();
        foreach (var m in data.EnumerateArray())
        {
            var ts = DateTimeOffset.FromUnixTimeSeconds(m.GetProperty("t").GetInt64()).ToString("yyyy-MM-dd HH:mm 'UTC'");
            sb.AppendLine($"[{ts}] {m.GetProperty("who").GetString()}: {m.GetProperty("body").GetString()}");
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Names of recent chats (display only — Copper never touches any but the active one).</summary>
    public async Task<JsonElement> ChatsAsync(int count)
    {
        var id = Interlocked.Increment(ref _nextId);
        return await RequestAsync(new { id, cmd = "chats", count }, id);
    }

    public async Task SendAsync(string text)
    {
        var id = Interlocked.Increment(ref _nextId);
        await RequestAsync(new { id, cmd = "send", text }, id);
    }

    public async Task SendMediaAsync(string path, string? caption)
    {
        var id = Interlocked.Increment(ref _nextId);
        await RequestAsync(new { id, cmd = "sendMedia", path, caption }, id);
    }

    public async Task<string> StatusAsync()
    {
        var id = Interlocked.Increment(ref _nextId);
        return (await RequestAsync(new { id, cmd = "status" }, id)).GetString() ?? "";
    }

    public void Dispose()
    {
        try { _proc?.StandardInput.Close(); _proc?.WaitForExit(3000); } catch { }
        try { if (_proc is { HasExited: false }) _proc.Kill(entireProcessTree: true); } catch { }
        _proc?.Dispose();
    }
}

using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace Copper;

/// <summary>Fan-out of server events (live feed, status, state changes) to the chat UI via SSE.</summary>
public sealed class SseHub
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> _clients = new();

    public void Broadcast(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        foreach (var client in _clients.Values)
            client.Writer.TryWrite(json);
    }

    public async Task ServeAsync(HttpContext ctx)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<string>();
        _clients[id] = channel;
        ctx.Response.Headers.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        try
        {
            await foreach (var msg in channel.Reader.ReadAllAsync(ctx.RequestAborted))
            {
                await ctx.Response.WriteAsync($"data: {msg}\n\n", ctx.RequestAborted);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
            }
        }
        catch (OperationCanceledException) { /* client left */ }
        finally
        {
            _clients.TryRemove(id, out _);
        }
    }
}

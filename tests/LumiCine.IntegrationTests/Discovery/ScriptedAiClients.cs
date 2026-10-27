using Microsoft.Extensions.AI;

namespace LumiCine.IntegrationTests.Discovery;

/// <summary>One scripted reply from the fake chat client, either text or a thrown failure.</summary>
internal sealed record ScriptedChatReply(string? Text = null, Exception? Failure = null);

/// <summary>
/// Deterministic IChatClient fake. Replays the last reply once the script runs out and records
/// every request so tests can assert prompts and structured-output options.
/// </summary>
internal sealed class ScriptedChatClient(params ScriptedChatReply[] replies) : IChatClient
{
    public List<(IList<ChatMessage> Messages, ChatOptions? Options)> Requests { get; } = [];

    private int _position;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        Requests.Add((messages.ToList(), options));
        var reply = NextReply();
        if (reply.Failure is not null) throw reply.Failure;
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply.Text ?? string.Empty)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add((messages.ToList(), options));
        var reply = NextReply();
        if (reply.Failure is not null) throw reply.Failure;
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, reply.Text ?? string.Empty);
    }

    private ScriptedChatReply NextReply()
    {
        var reply = replies[Math.Min(_position, replies.Length - 1)];
        _position++;
        return reply;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

/// <summary>Deterministic embedding fake returning one fixed vector.</summary>
internal sealed class ScriptedEmbeddingGenerator(float[] vector) : IEmbeddingGenerator<string, Embedding<float>>
{
    public List<string> Requests { get; } = [];

    public Exception? Failure { get; init; }

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var inputs = values.ToList();
        Requests.AddRange(inputs);
        if (Failure is not null) throw Failure;
        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(vector)]));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

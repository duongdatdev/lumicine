using Microsoft.Extensions.AI;

namespace LumiCine.Infrastructure.Discovery;

/// <summary>
/// Holds the Gemini-backed Microsoft.Extensions.AI adapters. Missing credentials yield null
/// clients so discovery degrades to keyword search instead of failing at startup.
/// </summary>
public sealed class GeminiAiClients(IChatClient? chatClient, IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator)
{
    public static readonly GeminiAiClients Unconfigured = new(null, null);

    public IChatClient? ChatClient { get; } = chatClient;

    public IEmbeddingGenerator<string, Embedding<float>>? EmbeddingGenerator { get; } = embeddingGenerator;

    public bool IsConfigured => ChatClient is not null && EmbeddingGenerator is not null;
}

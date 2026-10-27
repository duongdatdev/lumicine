namespace LumiCine.Infrastructure.Discovery;

/// <summary>
/// Gemini credentials and model choices for the discovery pipeline. The API key lives in user
/// secrets per environment; every timeout is a per-step budget inside the 30 second turn budget.
/// </summary>
public sealed class GeminiOptions
{
    public const string SectionName = "Ai:Gemini";

    public string? ApiKey { get; init; }

    public string ChatModel { get; init; } = "gemini-3.5-flash-lite";

    public string EmbeddingModel { get; init; } = "gemini-embedding-2";

    public int EmbeddingDimensions { get; init; } = 768;

    public int IntentMaxOutputTokens { get; init; } = 512;

    public int AnswerMaxOutputTokens { get; init; } = 800;

    public int IntentTimeoutSeconds { get; init; } = 15;

    public int AnswerTimeoutSeconds { get; init; } = 12;

    public int EmbeddingTimeoutSeconds { get; init; } = 10;

    /// <summary>Whole-turn budget across parsing, retrieval and answer writing.</summary>
    public int TotalTimeoutSeconds { get; init; } = 30;

    /// <summary>Hard cap for catalog document text fed to the embedding model.</summary>
    public int DocumentMaxCharacters { get; init; } = 2000;
}

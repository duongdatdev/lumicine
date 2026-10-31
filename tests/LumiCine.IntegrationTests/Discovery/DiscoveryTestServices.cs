using LumiCine.Application.Discovery;
using LumiCine.Infrastructure.Discovery;
using LumiCine.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LumiCine.IntegrationTests.Discovery;

internal static class DiscoveryTestServices
{
    /// <summary>Fixed embedding vector matching the configured Gemini dimensions.</summary>
    public static float[] DefaultVector() => Enumerable.Repeat(0.1f, 768).ToArray();

    /// <summary>
    /// Wires the conversation service with real stores/resolvers/search over the given context
    /// and scripted AI clients; one shared chat script serves parser first, writer second.
    /// </summary>
    public static DiscoveryConversationService CreateConversationService(
        LumiCineDbContext db, ScriptedChatClient chat, TimeProvider? clock = null)
    {
        var clockValue = clock ?? TimeProvider.System;
        var conversationOptions = Options.Create(new DiscoveryConversationOptions());
        var geminiOptions = Options.Create(new GeminiOptions());
        var aiClients = new GeminiAiClients(chat, new ScriptedEmbeddingGenerator(DefaultVector()));
        return new DiscoveryConversationService(
            db, clockValue, conversationOptions, geminiOptions,
            new DiscoveryConversationTurnStore(db, clockValue, conversationOptions),
            new DiscoveryIntentParser(aiClients, geminiOptions, NullLogger<DiscoveryIntentParser>.Instance),
            new DiscoveryFilterResolver(db, clockValue),
            new DiscoverySearchService(db, clockValue, aiClients, geminiOptions, conversationOptions),
            new DiscoveryAnswerWriter(aiClients, geminiOptions, NullLogger<DiscoveryAnswerWriter>.Instance));
    }

    /// <summary>Convenience overload for tests that only exercise CRUD paths (no AI call).</summary>
    public static DiscoveryConversationService CreateConversationService(LumiCineDbContext db, TimeProvider? clock = null) =>
        CreateConversationService(db, new ScriptedChatClient(), clock);
}

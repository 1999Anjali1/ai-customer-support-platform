using System.ClientModel;
using System.ClientModel.Primitives;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using SmartSupport.Application.Abstractions;

namespace SmartSupport.Infrastructure.Ai;

/// <summary>
/// "AI" section of appsettings / environment variables.
/// Defaults to Google Gemini (free tier) through its OpenAI-compatible endpoint.
/// Any OpenAI-compatible provider also works: OpenAI, Azure OpenAI, Groq, Ollama, LM Studio...
/// One API key is enough: if no separate embeddings key is set and both use the same endpoint, the chat key is reused.
/// </summary>
public sealed class AiOptions
{
    public const string Section = "AI";

    public ChatModelOptions Chat { get; set; } = new();
    public EmbeddingModelOptions Embeddings { get; set; } = new();
}

public sealed class ChatModelOptions
{
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gemini-3.1-flash-lite";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class EmbeddingModelOptions
{
    /// <summary>When false, knowledge search uses PostgreSQL full-text search only.</summary>
    public bool Enabled { get; set; } = true;
    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta/openai/";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gemini-embedding-001";

    /// <summary>
    /// Vector size requested from the provider. gemini-embedding-001 and OpenAI text-embedding-3-* can return 768 to match
    /// the database column. Set to 0 for models that don't support it (e.g. Ollama nomic-embed-text is already 768).
    /// </summary>
    public int? Dimensions { get; set; } = 768;
}

public static class AiSetup
{
    /// <summary>
    /// Registers <see cref="IChatClient"/> (Microsoft.Extensions.AI) with automatic function invocation,
    /// so the support agent's C# tools are called whenever the model asks for them.
    /// </summary>
    public static IServiceCollection AddAi(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AiOptions.Section);
        services.Configure<AiOptions>(section);
        var ai = section.Get<AiOptions>() ?? new AiOptions();

        // One key for everything: reuse the chat key for embeddings on the same provider.
        if (string.IsNullOrWhiteSpace(ai.Embeddings.ApiKey) &&
            string.Equals(ai.Embeddings.Endpoint.TrimEnd('/'), ai.Chat.Endpoint.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            ai.Embeddings.ApiKey = ai.Chat.ApiKey;
            services.PostConfigure<AiOptions>(o =>
            {
                if (string.IsNullOrWhiteSpace(o.Embeddings.ApiKey)) o.Embeddings.ApiKey = o.Chat.ApiKey;
            });
        }

        IChatClient innerClient = new NotConfiguredChatClient();
        if (ai.Chat.IsConfigured)
        {
            var chatOptions = new OpenAIClientOptions { Endpoint = new Uri(ai.Chat.Endpoint) };
            if (GeminiThoughtSignaturePolicy.IsGeminiEndpoint(ai.Chat.Endpoint))
                chatOptions.AddPolicy(new GeminiThoughtSignaturePolicy(), PipelinePosition.PerCall);

            innerClient = new OpenAIClient(new ApiKeyCredential(ai.Chat.ApiKey!), chatOptions)
                .GetChatClient(ai.Chat.Model)
                .AsIChatClient();
        }

        services.AddChatClient(innerClient)
            .UseFunctionInvocation();

        // Embeddings need a key (any value works for local Ollama). Without one, search falls back to keywords only.
        if (ai.Embeddings.Enabled && !string.IsNullOrWhiteSpace(ai.Embeddings.ApiKey))
        {
            var generator = new OpenAIClient(
                    new ApiKeyCredential(ai.Embeddings.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(ai.Embeddings.Endpoint) })
                .GetEmbeddingClient(ai.Embeddings.Model)
                .AsIEmbeddingGenerator();

            services.AddSingleton(generator);
            services.AddSingleton<ITextEmbedder, TextEmbedder>();
        }
        else
        {
            services.AddSingleton<ITextEmbedder, NullTextEmbedder>();
        }

        return services;
    }
}

public sealed class TextEmbedder(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    IOptions<AiOptions> options) : ITextEmbedder
{
    public bool IsEnabled => true;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0) return [];

        var dims = options.Value.Embeddings.Dimensions;
        var generationOptions = dims is > 0 ? new EmbeddingGenerationOptions { Dimensions = dims } : null;

        var embeddings = await generator.GenerateAsync(texts, generationOptions, cancellationToken);
        return embeddings.Select(e => e.Vector.ToArray()).ToList();
    }
}

public sealed class NullTextEmbedder : ITextEmbedder
{
    public bool IsEnabled => false;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<float[]>>([]);
}

/// <summary>
/// Used when no API key is set, so the app still starts and explains what to do
/// instead of crashing.
/// </summary>
public sealed class NotConfiguredChatClient : IChatClient
{
    private const string Text =
        "⚙️ Demo mode: the AI model is not configured yet. Add a free Gemini API key as GEMINI_API_KEY in the .env file " +
        "(or AI__Chat__ApiKey) and restart the API.";

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Text)));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, Text);
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }
}

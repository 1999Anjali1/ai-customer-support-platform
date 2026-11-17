using System.ClientModel;
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
/// Any OpenAI-compatible provider works: Groq (default, free tier), OpenAI, Azure OpenAI, Ollama, LM Studio...
/// </summary>
public sealed class AiOptions
{
    public const string Section = "AI";

    public ChatModelOptions Chat { get; set; } = new();
    public EmbeddingModelOptions Embeddings { get; set; } = new();
}

public sealed class ChatModelOptions
{
    public string Endpoint { get; set; } = "https://api.groq.com/openai/v1";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "llama-3.3-70b-versatile";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed class EmbeddingModelOptions
{
    /// <summary>When false, knowledge search uses PostgreSQL full-text search only.</summary>
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = "http://localhost:11434/v1";
    public string? ApiKey { get; set; } = "ollama";
    public string Model { get; set; } = "nomic-embed-text";

    /// <summary>Optional: ask the provider for a specific vector size (OpenAI text-embedding-3-* supports this).</summary>
    public int? Dimensions { get; set; }
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

        IChatClient innerClient = ai.Chat.IsConfigured
            ? new OpenAIClient(
                    new ApiKeyCredential(ai.Chat.ApiKey!),
                    new OpenAIClientOptions { Endpoint = new Uri(ai.Chat.Endpoint) })
                .GetChatClient(ai.Chat.Model)
                .AsIChatClient()
            : new NotConfiguredChatClient();

        services.AddChatClient(innerClient)
            .UseFunctionInvocation();

        if (ai.Embeddings.Enabled)
        {
            var generator = new OpenAIClient(
                    new ApiKeyCredential(string.IsNullOrWhiteSpace(ai.Embeddings.ApiKey) ? "none" : ai.Embeddings.ApiKey),
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
        var generationOptions = dims is null ? null : new EmbeddingGenerationOptions { Dimensions = dims };

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
        "⚙️ Demo mode: the AI model is not configured yet. Add a free Groq API key as GROQ_API_KEY in the .env file " +
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

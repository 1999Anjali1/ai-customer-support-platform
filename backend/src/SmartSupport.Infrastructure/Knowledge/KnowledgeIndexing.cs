using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pgvector;
using SmartSupport.Application.Abstractions;
using SmartSupport.Application.Knowledge;
using SmartSupport.Domain.Entities;
using SmartSupport.Domain.Enums;
using SmartSupport.Infrastructure.Persistence;

namespace SmartSupport.Infrastructure.Knowledge;

/// <summary>In-memory queue (System.Threading.Channels) between the API and the indexing worker.</summary>
public sealed class KnowledgeIndexQueue : IKnowledgeIndexQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid articleId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(articleId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Background worker: chunks each article, creates embeddings in batches and stores
/// them in PostgreSQL (pgvector). Runs outside the HTTP request so uploads are instant.
/// </summary>
public sealed class KnowledgeIndexingWorker(
    IServiceScopeFactory scopeFactory,
    IKnowledgeIndexQueue queue,
    ILogger<KnowledgeIndexingWorker> logger) : BackgroundService
{
    private const int BatchSize = 16;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnqueuePendingArticlesAsync(stoppingToken);

        await foreach (var articleId in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await IndexAsync(articleId, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Indexing failed for article {ArticleId}", articleId);
                await MarkFailedAsync(articleId, ex.Message, stoppingToken);
            }
        }
    }

    private async Task EnqueuePendingArticlesAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pending = await db.KnowledgeArticles
                .Where(a => a.Status == ArticleStatus.Pending)
                .Select(a => a.Id)
                .ToListAsync(ct);

            foreach (var id in pending)
                await queue.EnqueueAsync(id, ct);

            if (pending.Count > 0)
                logger.LogInformation("Queued {Count} knowledge articles for indexing", pending.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not load pending knowledge articles");
        }
    }

    private async Task IndexAsync(Guid articleId, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var embedder = scope.ServiceProvider.GetRequiredService<ITextEmbedder>();

        var article = await db.KnowledgeArticles
            .Include(a => a.Chunks)
            .FirstOrDefaultAsync(a => a.Id == articleId, ct);

        if (article is null) return; // deleted meanwhile

        var pieces = TextChunker.Split($"{article.Title}\n\n{article.Content}");

        string? warning = null;
        IReadOnlyList<float[]>? vectors = null;
        if (embedder.IsEnabled)
        {
            try
            {
                var all = new List<float[]>();
                foreach (var batch in pieces.Chunk(BatchSize))
                    all.AddRange(await embedder.EmbedAsync(batch, ct));
                vectors = all;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Still index for keyword search; semantic search will be skipped for this article.
                warning = $"Embeddings unavailable ({ex.Message}). Keyword search only.";
                logger.LogWarning(ex, "Embedding failed for article {ArticleId}", articleId);
            }
        }

        db.KnowledgeChunks.RemoveRange(article.Chunks);

        for (var i = 0; i < pieces.Count; i++)
        {
            db.KnowledgeChunks.Add(new KnowledgeChunk
            {
                ArticleId = article.Id,
                ChunkIndex = i,
                Content = pieces[i],
                Embedding = vectors is not null && i < vectors.Count ? new Vector(vectors[i]) : null
            });
        }

        article.Status = ArticleStatus.Indexed;
        article.IndexingError = warning;
        article.IndexedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Indexed article '{Title}' into {Count} chunks (embeddings: {HasEmbeddings})",
            article.Title, pieces.Count, vectors is not null);
    }

    private async Task MarkFailedAsync(Guid articleId, string error, CancellationToken ct)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var article = await db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == articleId, ct);
            if (article is null) return;

            article.Status = ArticleStatus.Failed;
            article.IndexingError = error.Length > 500 ? error[..500] : error;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not mark article {ArticleId} as failed", articleId);
        }
    }
}

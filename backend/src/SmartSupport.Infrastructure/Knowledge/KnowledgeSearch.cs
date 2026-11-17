using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using SmartSupport.Application.Abstractions;
using SmartSupport.Application.Dtos;
using SmartSupport.Infrastructure.Persistence;

namespace SmartSupport.Infrastructure.Knowledge;

/// <summary>
/// Hybrid retrieval for RAG:
///  • Vector search (pgvector, cosine distance) finds chunks with the same MEANING
///    ("my internet is slow" ≈ "troubleshooting mobile data").
///  • Keyword search (PostgreSQL full-text) finds exact terms such as "eSIM" or "5G".
/// The two ranked lists are merged with Reciprocal Rank Fusion (RRF).
/// </summary>
public sealed class KnowledgeSearch(AppDbContext db, ITextEmbedder embedder, ILogger<KnowledgeSearch> logger) : IKnowledgeSearch
{
    /// <summary>Chunks further away than this (cosine distance) are treated as unrelated.</summary>
    private const double MaxCosineDistance = 0.65;

    public async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(string query, int top, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var candidates = Math.Max(top * 3, 10);
        var keyword = await KeywordSearchAsync(query, candidates, cancellationToken);
        var vector = await VectorSearchAsync(query, candidates, cancellationToken);

        var fused = RankFusion.Fuse(
            [new RankedList("keyword", keyword), new RankedList("vector", vector)],
            top);

        if (fused.Count == 0) return [];

        var ids = fused.Select(f => f.Id).ToList();
        var chunks = await db.KnowledgeChunks.AsNoTracking()
            .Include(c => c.Article)
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, cancellationToken);

        return fused
            .Where(f => chunks.ContainsKey(f.Id))
            .Select(f =>
            {
                var chunk = chunks[f.Id];
                return new KnowledgeHit(chunk.ArticleId, chunk.Article.Title, chunk.Content, Math.Round(f.Score, 4), f.MatchedBy);
            })
            .ToList();
    }

    private async Task<IReadOnlyList<Guid>> KeywordSearchAsync(string query, int limit, CancellationToken ct)
    {
        var tsQuery = TsQueryBuilder.Build(query);
        if (tsQuery is null) return [];

        var rows = await db.Database
            .SqlQuery<KeywordRow>($"""
                SELECT "Id", ts_rank("SearchVector", to_tsquery('simple', {tsQuery})) AS "Rank"
                FROM "KnowledgeChunks"
                WHERE "SearchVector" @@ to_tsquery('simple', {tsQuery})
                """)
            .ToListAsync(ct);

        return rows.OrderByDescending(r => r.Rank).Take(limit).Select(r => r.Id).ToList();
    }

    private async Task<IReadOnlyList<Guid>> VectorSearchAsync(string query, int limit, CancellationToken ct)
    {
        if (!embedder.IsEnabled) return [];

        try
        {
            var embedding = (await embedder.EmbedAsync([query], ct))[0];
            var queryVector = new Vector(embedding);

            var rows = await db.KnowledgeChunks.AsNoTracking()
                .Where(c => c.Embedding != null)
                .Select(c => new { c.Id, Distance = c.Embedding!.CosineDistance(queryVector) })
                .OrderBy(x => x.Distance)
                .Take(limit)
                .ToListAsync(ct);

            return rows.Where(r => r.Distance <= MaxCosineDistance).Select(r => r.Id).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The embedding service being down must not break the chat – keyword search still works.
            logger.LogWarning(ex, "Vector search failed, falling back to keyword search only");
            return [];
        }
    }
}

/// <summary>Row shape for the raw full-text SQL query.</summary>
internal sealed class KeywordRow
{
    public Guid Id { get; set; }
    public float Rank { get; set; }
}

public sealed record RankedList(string Name, IReadOnlyList<Guid> Ids);

public sealed record FusedResult(Guid Id, double Score, string MatchedBy);

/// <summary>
/// Reciprocal Rank Fusion: score = Σ 1 / (k + rank). Documents that rank well in
/// both lists win; no need to normalise scores from different search engines.
/// </summary>
public static class RankFusion
{
    public static IReadOnlyList<FusedResult> Fuse(IReadOnlyList<RankedList> lists, int top, int k = 60)
    {
        var scores = new Dictionary<Guid, (double Score, List<string> Sources)>();

        foreach (var list in lists)
        {
            for (var rank = 0; rank < list.Ids.Count; rank++)
            {
                var id = list.Ids[rank];
                var add = 1.0 / (k + rank + 1);
                if (scores.TryGetValue(id, out var existing))
                {
                    existing.Sources.Add(list.Name);
                    scores[id] = (existing.Score + add, existing.Sources);
                }
                else
                {
                    scores[id] = (add, [list.Name]);
                }
            }
        }

        return scores
            .OrderByDescending(s => s.Value.Score)
            .Take(top)
            .Select(s => new FusedResult(s.Key, s.Value.Score, string.Join('+', s.Value.Sources)))
            .ToList();
    }
}

/// <summary>
/// Turns a free-text question into a safe PostgreSQL tsquery: "How do I activate eSIM?"
/// → "activate:* | esim:*". Only letters/digits survive, so user input can't break the SQL.
/// </summary>
public static partial class TsQueryBuilder
{
    private static readonly HashSet<string> StopWords =
    [
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "can", "do", "does", "for", "from", "get", "have",
        "how", "i", "in", "is", "it", "me", "my", "of", "on", "or", "please", "so", "the", "this", "to", "was",
        "what", "when", "where", "which", "who", "why", "will", "with", "you", "your", "want", "need", "hi", "hello",
        "في", "من", "على", "إلى", "عن", "هل", "ما", "كيف", "أن", "او", "أو", "و", "مع", "هذا", "هذه"
    ];

    public static string? Build(string? text, int maxTerms = 12)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var terms = Word().Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => w.Length >= 2 && !StopWords.Contains(w))
            .Distinct()
            .Take(maxTerms)
            .Select(w => $"{w}:*")
            .ToList();

        return terms.Count == 0 ? null : string.Join(" | ", terms);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
}

using Pgvector;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Domain.Entities;

/// <summary>A help-centre article the AI can search (RAG source).</summary>
public class KnowledgeArticle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public required string Category { get; set; }
    public string Language { get; set; } = "en";
    public required string Content { get; set; }

    public ArticleStatus Status { get; set; } = ArticleStatus.Pending;
    public string? IndexingError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? IndexedAt { get; set; }

    public List<KnowledgeChunk> Chunks { get; set; } = [];
}

/// <summary>
/// A small piece of an article. Each chunk is stored with a vector embedding
/// (semantic search) and a PostgreSQL full-text index (keyword search).
/// </summary>
public class KnowledgeChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ArticleId { get; set; }
    public KnowledgeArticle Article { get; set; } = null!;
    public int ChunkIndex { get; set; }
    public required string Content { get; set; }

    /// <summary>Embedding vector (null if no embedding model is configured).</summary>
    public Vector? Embedding { get; set; }
}

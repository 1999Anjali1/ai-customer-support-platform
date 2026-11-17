using Microsoft.EntityFrameworkCore;
using SmartSupport.Application.Abstractions;
using SmartSupport.Application.Dtos;
using SmartSupport.Domain.Common;
using SmartSupport.Domain.Entities;
using SmartSupport.Domain.Enums;

namespace SmartSupport.Application.Knowledge;

/// <summary>Admin use cases for the help-centre articles used by the AI (RAG).</summary>
public sealed class KnowledgeService(IAppDbContext db, IKnowledgeIndexQueue queue, IKnowledgeSearch search)
{
    public async Task<IReadOnlyList<KnowledgeArticleDto>> ListAsync(CancellationToken ct = default) =>
        await db.KnowledgeArticles.AsNoTracking()
            .OrderBy(a => a.Category).ThenBy(a => a.Title)
            .Select(a => new KnowledgeArticleDto(
                a.Id, a.Title, a.Category, a.Language, a.Content, a.Status, a.IndexingError,
                a.Chunks.Count, a.CreatedAt, a.IndexedAt))
            .ToListAsync(ct);

    public async Task<KnowledgeArticleDto> CreateAsync(CreateArticleRequest request, CancellationToken ct = default)
    {
        Validate(request);

        var article = new KnowledgeArticle
        {
            Title = request.Title.Trim(),
            Category = request.Category.Trim(),
            Language = request.Language == "ar" ? "ar" : "en",
            Content = request.Content.Trim()
        };

        db.KnowledgeArticles.Add(article);
        await db.SaveChangesAsync(ct);

        // Chunking + embedding happens in the background so the request returns immediately.
        await queue.EnqueueAsync(article.Id, ct);

        return new KnowledgeArticleDto(article.Id, article.Title, article.Category, article.Language, article.Content,
            article.Status, null, 0, article.CreatedAt, null);
    }

    public async Task UpdateAsync(Guid id, CreateArticleRequest request, CancellationToken ct = default)
    {
        Validate(request);
        var article = await db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == id, ct)
                      ?? throw new NotFoundException("Article not found.");

        article.Title = request.Title.Trim();
        article.Category = request.Category.Trim();
        article.Language = request.Language == "ar" ? "ar" : "en";
        article.Content = request.Content.Trim();
        article.Status = ArticleStatus.Pending;
        article.IndexingError = null;

        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(article.Id, ct);
    }

    public async Task ReindexAsync(Guid id, CancellationToken ct = default)
    {
        var article = await db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == id, ct)
                      ?? throw new NotFoundException("Article not found.");

        article.Status = ArticleStatus.Pending;
        article.IndexingError = null;
        await db.SaveChangesAsync(ct);
        await queue.EnqueueAsync(article.Id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var article = await db.KnowledgeArticles.FirstOrDefaultAsync(a => a.Id == id, ct)
                      ?? throw new NotFoundException("Article not found.");

        db.KnowledgeArticles.Remove(article); // chunks are removed by cascade delete
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Lets an admin test what the AI would retrieve for a question.</summary>
    public Task<IReadOnlyList<KnowledgeHit>> SearchAsync(string query, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(query)
            ? Task.FromResult<IReadOnlyList<KnowledgeHit>>([])
            : search.SearchAsync(query, top: 5, ct);

    private static void Validate(CreateArticleRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Title) || r.Title.Length > 200)
            throw new DomainException("Title is required (max 200 characters).");
        if (string.IsNullOrWhiteSpace(r.Category))
            throw new DomainException("Category is required.");
        if (string.IsNullOrWhiteSpace(r.Content) || r.Content.Length < 30)
            throw new DomainException("Content must be at least 30 characters.");
        if (r.Content.Length > 50_000)
            throw new DomainException("Content is too long (max 50,000 characters).");
    }
}

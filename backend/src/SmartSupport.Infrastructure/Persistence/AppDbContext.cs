using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NpgsqlTypes;
using SmartSupport.Application.Abstractions;
using SmartSupport.Domain.Entities;

namespace SmartSupport.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<CustomerAccount> CustomerAccounts => Set<CustomerAccount>();
    public DbSet<Bill> Bills => Set<Bill>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();
    public DbSet<KnowledgeChunk> KnowledgeChunks => Set<KnowledgeChunk>();

    /// <summary>Must match the embedding model's output size (nomic-embed-text = 768).</summary>
    public const int EmbeddingDimensions = 768;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // pgvector extension for semantic search
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<User>(b =>
        {
            b.Property(u => u.Email).HasMaxLength(200);
            b.HasIndex(u => u.Email).IsUnique();
            b.Property(u => u.FullName).HasMaxLength(150);
            b.Property(u => u.PreferredLanguage).HasMaxLength(2);
            b.HasOne(u => u.CustomerAccount).WithMany().HasForeignKey(u => u.CustomerAccountId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Plan>(b =>
        {
            b.Property(p => p.Code).HasMaxLength(30);
            b.HasIndex(p => p.Code).IsUnique();
            b.Property(p => p.NameEn).HasMaxLength(100);
            b.Property(p => p.NameAr).HasMaxLength(100);
            b.Property(p => p.MonthlyPriceQar).HasPrecision(10, 2);
        });

        modelBuilder.Entity<CustomerAccount>(b =>
        {
            b.Property(a => a.AccountNumber).HasMaxLength(20);
            b.HasIndex(a => a.AccountNumber).IsUnique();
            b.Property(a => a.MobileNumber).HasMaxLength(20);
            b.Property(a => a.FullName).HasMaxLength(150);
            b.Property(a => a.DataUsedGb).HasPrecision(8, 2);
            b.Ignore(a => a.HasOverdueBills);
            b.HasOne(a => a.Plan).WithMany().HasForeignKey(a => a.PlanId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(a => a.Bills).WithOne().HasForeignKey(x => x.CustomerAccountId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Bill>(b =>
        {
            b.Property(x => x.AmountQar).HasPrecision(10, 2);
            b.HasIndex(x => new { x.CustomerAccountId, x.Period }).IsUnique();
        });

        modelBuilder.Entity<SupportTicket>(b =>
        {
            b.Property(t => t.Number).HasMaxLength(20);
            b.HasIndex(t => t.Number).IsUnique();
            b.Property(t => t.Subject).HasMaxLength(120);
            b.Property(t => t.Description).HasMaxLength(4000);
            b.HasOne(t => t.CustomerAccount).WithMany().HasForeignKey(t => t.CustomerAccountId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(t => t.Status);
        });

        modelBuilder.Entity<Conversation>(b =>
        {
            b.Property(c => c.Language).HasMaxLength(2);
            b.Property(c => c.EscalationReason).HasMaxLength(500);
            b.Property(c => c.Topic).HasMaxLength(40);
            b.HasOne(c => c.Customer).WithMany().HasForeignKey(c => c.CustomerUserId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(c => c.AssignedAgent).WithMany().HasForeignKey(c => c.AssignedAgentId).OnDelete(DeleteBehavior.SetNull);
            b.HasMany(c => c.Messages).WithOne().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(c => c.Status);
            b.HasIndex(c => new { c.CustomerUserId, c.LastMessageAt });
        });

        modelBuilder.Entity<Message>(b =>
        {
            b.Property(m => m.Content).HasMaxLength(8000);
            b.HasIndex(m => new { m.ConversationId, m.CreatedAt });
        });

        modelBuilder.Entity<KnowledgeArticle>(b =>
        {
            b.Property(a => a.Title).HasMaxLength(200);
            b.Property(a => a.Category).HasMaxLength(60);
            b.Property(a => a.Language).HasMaxLength(2);
            b.HasMany(a => a.Chunks).WithOne(c => c.Article).HasForeignKey(c => c.ArticleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KnowledgeChunk>(b =>
        {
            b.HasIndex(c => new { c.ArticleId, c.ChunkIndex });

            // Semantic search: vector column + HNSW index using cosine distance.
            b.Property(c => c.Embedding).HasColumnType($"vector({EmbeddingDimensions})");
            b.HasIndex(c => c.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");

            // Keyword search: a generated tsvector column with a GIN index (PostgreSQL full-text search).
            // 'simple' config works for both English and Arabic text.
            b.Property<NpgsqlTsVector>("SearchVector")
                .HasComputedColumnSql("to_tsvector('simple', \"Content\")", stored: true);
            b.HasIndex("SearchVector").HasMethod("GIN");
        });

        ApplyConventions(modelBuilder);
    }

    /// <summary>
    /// 1. Store every enum as readable text ("WaitingForAgent") instead of a number.
    /// 2. Guid keys are generated in C# (Guid.NewGuid()), so EF must not treat a set key
    ///    as "already saved" when a new child is added to a tracked parent's collection.
    /// </summary>
    private static void ApplyConventions(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

                if (type.IsEnum)
                {
                    var converter = (ValueConverter)Activator.CreateInstance(
                        typeof(EnumToStringConverter<>).MakeGenericType(type))!;
                    property.SetValueConverter(converter);
                    property.SetMaxLength(32);
                }

                if (property.Name == "Id" && type == typeof(Guid))
                    property.ValueGenerated = ValueGenerated.Never;
            }
        }
    }
}

using Microsoft.Extensions.AI;
using SmartSupport.Application.Chat;
using SmartSupport.Application.Insights;
using SmartSupport.Application.Knowledge;
using SmartSupport.Domain.Entities;
using SmartSupport.Domain.Enums;
using SmartSupport.Infrastructure.Knowledge;

namespace SmartSupport.UnitTests;

public class TextChunkerTests
{
    [Fact]
    public void Short_text_becomes_a_single_chunk()
    {
        var chunks = TextChunker.Split("Bills are due on the 15th of each month.");

        Assert.Single(chunks);
    }

    [Fact]
    public void Empty_text_gives_no_chunks()
    {
        Assert.Empty(TextChunker.Split("   "));
    }

    [Fact]
    public void Long_text_is_split_into_chunks_within_the_size_limit()
    {
        var paragraphs = Enumerable.Range(1, 30)
            .Select(i => $"Paragraph {i}. " + string.Join(' ', Enumerable.Repeat("roaming pack data minutes", 8)));
        var text = string.Join("\n\n", paragraphs);

        var chunks = TextChunker.Split(text, maxChars: 500, overlapChars: 100);

        Assert.True(chunks.Count > 1);
        // overlap may push a chunk slightly past maxChars, but never by more than the overlap
        Assert.All(chunks, c => Assert.True(c.Length <= 600, $"chunk too long: {c.Length}"));
        Assert.Contains("Paragraph 1.", chunks[0]);
        Assert.Contains("Paragraph 30.", chunks[^1]);
    }

    [Fact]
    public void Consecutive_chunks_overlap_to_keep_context()
    {
        var text = string.Join("\n\n", Enumerable.Range(1, 10).Select(i => $"Sentence number {i} about eSIM activation steps."));

        var chunks = TextChunker.Split(text, maxChars: 120, overlapChars: 40);

        Assert.True(chunks.Count > 1);
        var tailOfFirst = chunks[0][^20..];
        Assert.Contains(tailOfFirst, chunks[1]);
    }

    [Fact]
    public void Invalid_overlap_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Split("text", maxChars: 100, overlapChars: 100));
    }
}

public class LanguageDetectorTests
{
    [Theory]
    [InlineData("What is my bill?", "en")]
    [InlineData("كم فاتورتي هذا الشهر؟", "ar")]
    [InlineData("أريد تفعيل eSIM", "ar")]
    [InlineData("12345 ???", "en")]
    public void Detects_language(string text, string expected)
    {
        Assert.Equal(expected, LanguageDetector.Detect(text));
    }
}

public class TsQueryBuilderTests
{
    [Fact]
    public void Builds_prefix_or_query_without_stop_words()
    {
        var query = TsQueryBuilder.Build("How do I activate my eSIM?");

        Assert.Equal("activate:* | esim:*", query);
    }

    [Fact]
    public void Strips_characters_that_could_break_the_sql_query()
    {
        var query = TsQueryBuilder.Build("roaming'); DROP TABLE x; -- & | !");

        Assert.NotNull(query);
        Assert.DoesNotContain("'", query);
        Assert.DoesNotContain(";", query);
        Assert.DoesNotContain("!", query);
    }

    [Fact]
    public void Works_for_arabic()
    {
        Assert.Equal("التجوال:* | السعودية:*", TsQueryBuilder.Build("التجوال في السعودية"));
    }

    [Fact]
    public void Returns_null_when_nothing_searchable_remains()
    {
        Assert.Null(TsQueryBuilder.Build("how do I ?"));
    }
}

public class RankFusionTests
{
    [Fact]
    public void Item_found_by_both_searches_ranks_first()
    {
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();

        var fused = RankFusion.Fuse(
        [
            new RankedList("keyword", [a, b]),
            new RankedList("vector", [c, b])
        ], top: 3);

        Assert.Equal(b, fused[0].Id);
        Assert.Equal("keyword+vector", fused[0].MatchedBy);
        Assert.Equal(3, fused.Count);
    }

    [Fact]
    public void Respects_top_limit()
    {
        var ids = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();

        var fused = RankFusion.Fuse([new RankedList("keyword", ids)], top: 4);

        Assert.Equal(4, fused.Count);
        Assert.Equal(ids[0], fused[0].Id);
    }
}

public class InsightParserTests
{
    [Fact]
    public void Parses_clean_json()
    {
        var (sentiment, topic) = InsightParser.Parse("""{"sentiment":"Negative","topic":"Network"}""");

        Assert.Equal(Sentiment.Negative, sentiment);
        Assert.Equal("Network", topic);
    }

    [Fact]
    public void Parses_json_wrapped_in_text_and_code_fences()
    {
        var (sentiment, topic) = InsightParser.Parse("Sure! ```json\n{\"sentiment\": \"positive\", \"topic\": \"roaming\"}\n```");

        Assert.Equal(Sentiment.Positive, sentiment);
        Assert.Equal("Roaming", topic);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("{broken")]
    [InlineData("""{"sentiment":"Angry","topic":"Weather"}""")]
    public void Falls_back_to_safe_defaults(string? raw)
    {
        var (sentiment, topic) = InsightParser.Parse(raw);

        Assert.Equal(Sentiment.Neutral, sentiment);
        Assert.Equal("Other", topic);
    }
}

public class PromptBuilderTests
{
    [Fact]
    public void Prompt_has_system_message_then_history_and_skips_system_notes()
    {
        var customer = new User { Email = "c@demo.qa", FullName = "Test Customer", Role = UserRole.Customer };
        var conversation = Conversation.Start(customer.Id, "en");
        conversation.AddMessage(MessageSender.Assistant, "Hi! How can I help?");
        conversation.AddMessage(MessageSender.Customer, "What's my bill?");
        conversation.AddMessage(MessageSender.System, "Transferred to an agent");
        conversation.AddMessage(MessageSender.Agent, "Hello, Sara here.");

        var prompt = SupportAgent.BuildPrompt(conversation, customer, account: null);

        Assert.Equal(4, prompt.Count);
        Assert.Equal(ChatRole.System, prompt[0].Role);
        Assert.Contains("Test Customer", prompt[0].Text);
        Assert.Equal(ChatRole.User, prompt[2].Role);
        Assert.StartsWith("[Human agent]", prompt[3].Text);
    }

    [Fact]
    public void Welcome_message_is_localised()
    {
        Assert.Contains("Hi Fatima", Prompts.Welcome("Fatima Al-Marri", "en"));
        Assert.Contains("مرحباً Fatima", Prompts.Welcome("Fatima Al-Marri", "ar"));
    }
}

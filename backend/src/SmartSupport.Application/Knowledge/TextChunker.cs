using System.Text;
using System.Text.RegularExpressions;

namespace SmartSupport.Application.Knowledge;

/// <summary>
/// Splits an article into overlapping chunks for RAG.
///
/// Why chunk? Embedding a whole article gives one blurry vector; small chunks
/// (a few paragraphs) let search return exactly the part that answers the question.
/// Overlap keeps a sentence that sits on a boundary from losing its context.
/// </summary>
public static partial class TextChunker
{
    public static IReadOnlyList<string> Split(string text, int maxChars = 800, int overlapChars = 150)
    {
        if (maxChars <= 0) throw new ArgumentOutOfRangeException(nameof(maxChars));
        if (overlapChars < 0 || overlapChars >= maxChars) throw new ArgumentOutOfRangeException(nameof(overlapChars));
        if (string.IsNullOrWhiteSpace(text)) return [];

        var normalized = text.Replace("\r\n", "\n").Trim();
        var paragraphs = ParagraphSplit().Split(normalized)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .SelectMany(p => p.Length <= maxChars ? new[] { p } : SplitLongParagraph(p, maxChars))
            .ToList();

        var chunks = new List<string>();
        var current = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (current.Length > 0 && current.Length + paragraph.Length + 2 > maxChars)
            {
                var chunk = current.ToString().Trim();
                chunks.Add(chunk);

                current.Clear();
                var tail = Tail(chunk, overlapChars);
                if (tail.Length > 0) current.Append(tail).Append("\n\n");
            }

            current.Append(paragraph).Append("\n\n");
        }

        if (current.Length > 0)
            chunks.Add(current.ToString().Trim());

        return chunks;
    }

    /// <summary>Splits a very long paragraph on sentence boundaries.</summary>
    private static IEnumerable<string> SplitLongParagraph(string paragraph, int maxChars)
    {
        var sentences = SentenceSplit().Split(paragraph);
        var sb = new StringBuilder();

        foreach (var sentence in sentences)
        {
            if (sb.Length > 0 && sb.Length + sentence.Length + 1 > maxChars)
            {
                yield return sb.ToString().Trim();
                sb.Clear();
            }

            if (sentence.Length > maxChars)
            {
                // A single huge "sentence" (e.g. a table row dump) – hard cut.
                for (var i = 0; i < sentence.Length; i += maxChars)
                    yield return sentence.Substring(i, Math.Min(maxChars, sentence.Length - i));
                continue;
            }

            sb.Append(sentence).Append(' ');
        }

        if (sb.Length > 0) yield return sb.ToString().Trim();
    }

    /// <summary>Last ~N characters, starting at a word boundary.</summary>
    private static string Tail(string text, int length)
    {
        if (length == 0 || text.Length <= length) return string.Empty;
        var tail = text[^length..];
        var space = tail.IndexOf(' ');
        return space >= 0 && space < tail.Length - 1 ? tail[(space + 1)..] : tail;
    }

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex ParagraphSplit();

    [GeneratedRegex(@"(?<=[\.\!\?؟])\s+")]
    private static partial Regex SentenceSplit();
}

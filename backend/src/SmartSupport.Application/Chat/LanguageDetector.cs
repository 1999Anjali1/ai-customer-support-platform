namespace SmartSupport.Application.Chat;

/// <summary>
/// Very small language detector: if most letters are in the Arabic Unicode block the
/// message is Arabic, otherwise English. Good enough to switch the UI to RTL and to
/// tell the AI which language to answer in.
/// </summary>
public static class LanguageDetector
{
    public static string Detect(string? text, string fallback = "en")
    {
        if (string.IsNullOrWhiteSpace(text)) return fallback;

        int arabic = 0, latin = 0;
        foreach (var ch in text)
        {
            if (ch is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ') arabic++;
            else if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z') latin++;
        }

        if (arabic == 0 && latin == 0) return fallback;
        return arabic >= latin ? "ar" : "en";
    }
}

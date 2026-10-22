using System.Text;
using System.Text.RegularExpressions;

namespace Dawwerli.Domain.Rules;

/// <summary>Rejects phone numbers (any digit script, spaced), links and @handles.</summary>
public static partial class ContactFilter
{
    [GeneratedRegex(@"(?:\d[\s\-.]*){7,}")] private static partial Regex Phone();
    [GeneratedRegex(@"(https?://\S+|www\.\S+|\b\S+\.(com|net|org|iq|me|ly|co)\b)", RegexOptions.IgnoreCase)] private static partial Regex Link();
    [GeneratedRegex(@"@\w+")] private static partial Regex Handle();

    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            sb.Append(c switch
            {
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                _ => c,
            });
        }
        return sb.ToString();
    }

    public static bool HasContact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var n = Normalize(text);
        return Phone().IsMatch(n) || Link().IsMatch(n) || Handle().IsMatch(n);
    }

    /// <summary>Shown to the user when <see cref="HasContact"/> rejects a text.</summary>
    public const string ArabicMessage = "ممنوع تكتب رقم تلفون أو يوزر (@) أو رابط. كل التواصل والاتفاق يصير داخل دورلي.";

    public static string? FindBanned(string? text, IEnumerable<string> banned)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return banned.FirstOrDefault(w => text.Contains(w, StringComparison.OrdinalIgnoreCase));
    }
}

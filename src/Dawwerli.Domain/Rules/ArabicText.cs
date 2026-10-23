using System.Text;

namespace Dawwerli.Domain.Rules;

/// <summary>
/// Search folding for Arabic product names: hamza/madda alef forms → ا, ة → ه, ى/ئ/ی → ي, ؤ → و, ک/گ → ك,
/// Arabic-Indic digits → 0-9, ASCII A-Z → a-z, diacritics and tatweel dropped, whitespace collapsed.
/// Products store the folded name (<c>normalized_name</c>) and queries fold the search term the same way.
/// </summary>
/// <remarks>
/// Every step is a plain character map so the backfill migration can do exactly the same in SQL with translate();
/// a test checks both give the same result.
/// </remarks>
public static class ArabicText
{
    /// <summary>Characters mapped one to one onto <see cref="To"/> (same index).</summary>
    public const string From =
        "\u0623\u0625\u0622\u0671" + // أ إ آ ٱ
        "\u0629" + // ة
        "\u0649\u0626\u06CC" + // ى ئ ی
        "\u0624" + // ؤ
        "\u06A9\u06AF" + // ک گ
        "\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669" + // ٠-٩
        "\u06F0\u06F1\u06F2\u06F3\u06F4\u06F5\u06F6\u06F7\u06F8\u06F9" + // ۰-۹
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
        "\t\n\v\f\r\u00A0";

    public const string To =
        "\u0627\u0627\u0627\u0627" +
        "\u0647" +
        "\u064A\u064A\u064A" +
        "\u0648" +
        "\u0643\u0643" +
        "0123456789" +
        "0123456789" +
        "abcdefghijklmnopqrstuvwxyz" +
        "      ";

    /// <summary>Dropped: tashkeel (U+064B–U+065F), superscript alef (U+0670) and tatweel (U+0640).</summary>
    public const string Removed =
        "\u064B\u064C\u064D\u064E\u064F\u0650\u0651\u0652\u0653\u0654\u0655\u0656\u0657\u0658\u0659\u065A\u065B\u065C\u065D\u065E\u065F" +
        "\u0670\u0640";

    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        var space = true; // drops leading spaces and collapses runs
        foreach (var c in text)
        {
            if (Removed.Contains(c)) continue;
            var i = From.IndexOf(c);
            var m = i >= 0 ? To[i] : c;
            if (m == ' ')
            {
                if (space) continue;
                space = true;
            }
            else space = false;
            sb.Append(m);
        }
        if (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        return sb.ToString();
    }
}

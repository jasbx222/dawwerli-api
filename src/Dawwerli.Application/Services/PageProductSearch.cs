using System.Globalization;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Rules;

namespace Dawwerli.Application.Services;

public enum PageProductSort { Newest, PriceAsc, PriceDesc, BestSelling, TopRated }

/// <summary>Validated form of <see cref="PageProductQuery"/>; Term is already folded with <see cref="ArabicText.Normalize"/>.</summary>
public sealed record PageProductFilter(string? Term, IReadOnlyList<int> CategoryIds, long? MinPrice, long? MaxPrice, bool InStock, PageProductSort Sort);

/// <summary>Input rules for the page product search.</summary>
public static class PageProductSearch
{
    public const int MaxQueryLength = 60;
    public const int MaxCategories = 20;
    /// <summary>Prices are IQD; anything above this is a typo or a probe.</summary>
    public const long MaxPrice = 1_000_000_000_000;
    public const string LikeEscape = "\\";

    private static readonly Dictionary<string, PageProductSort> Sorts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["newest"] = PageProductSort.Newest,
        ["price_asc"] = PageProductSort.PriceAsc,
        ["price_desc"] = PageProductSort.PriceDesc,
        ["best_selling"] = PageProductSort.BestSelling,
        ["top_rated"] = PageProductSort.TopRated,
    };

    public static Result<PageProductFilter> Parse(PageProductQuery q)
    {
        var raw = q.Q?.Trim() ?? "";
        if (raw.Length > MaxQueryLength)
            return Error.Validation("products.q_too_long", $"Search text is limited to {MaxQueryLength} characters");
        var term = ArabicText.Normalize(raw);

        var cats = new List<int>();
        foreach (var part in (q.CategoryIds ?? []).SelectMany(v => (v ?? "").Split(',')))
        {
            var t = part.Trim();
            if (t.Length == 0) continue;
            if (!int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
                return Error.Validation("products.category_invalid", "categoryIds must be positive integers");
            if (!cats.Contains(id)) cats.Add(id);
            if (cats.Count > MaxCategories)
                return Error.Validation("products.too_many_categories", $"At most {MaxCategories} categories");
        }

        if (q.MinPrice is < 0 or > MaxPrice || q.MaxPrice is < 0 or > MaxPrice)
            return Error.Validation("products.price_invalid", $"Prices must be between 0 and {MaxPrice}");
        if (q.MinPrice is { } min && q.MaxPrice is { } max && min > max)
            return Error.Validation("products.price_range", "minPrice must not exceed maxPrice");

        var sort = PageProductSort.Newest;
        if (!string.IsNullOrWhiteSpace(q.Sort) && !Sorts.TryGetValue(q.Sort.Trim(), out sort))
            return Error.Validation("products.sort_invalid", "sort must be one of: " + string.Join(", ", Sorts.Keys));

        return new PageProductFilter(term.Length == 0 ? null : term, cats, q.MinPrice, q.MaxPrice, q.InStock == true, sort);
    }

    /// <summary>Escapes LIKE wildcards so the term matches literally (used with <see cref="LikeEscape"/>).</summary>
    public static string EscapeLike(string term) =>
        term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

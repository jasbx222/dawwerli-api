using System.Text.Json;
using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

public sealed class CatalogService(IAppDbContext db, IStorage storage, IRulesProvider rules)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Schema(string? Hint, string[]? Required, string[]? Sizes, string[]? Colors, long[]? PriceSuggestions);

    public static CategorySchemaDto ParseSchema(string json)
    {
        var s = JsonSerializer.Deserialize<Schema>(json, Json) ?? new Schema(null, null, null, null, null);
        return new CategorySchemaDto(s.Hint ?? "", s.Required ?? [], s.Sizes ?? [], s.Colors ?? [], s.PriceSuggestions ?? []);
    }

    public async Task<IReadOnlyList<CategoryDto>> CategoriesAsync(CancellationToken ct) =>
        (await db.Categories.Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync(ct))
        .Select(c => new CategoryDto(c.Id, c.Slug, c.Name, c.Emoji, storage.Url(c.ImageKey), c.Badge.Code(), ParseSchema(c.FieldSchema)))
        .ToList();

    public async Task<IReadOnlyList<GovernorateDto>> GovernoratesAsync(CancellationToken ct)
    {
        var adj = await db.GovernorateAdjacency.ToListAsync(ct);
        return (await db.Governorates.OrderBy(g => g.SortOrder).ToListAsync(ct))
            .Select(g => new GovernorateDto(g.Id, g.Name, adj.Where(a => a.GovernorateId == g.Id).Select(a => a.NeighborId).ToList()))
            .ToList();
    }

    /// <summary>Keyword suggester (ICategorySuggester in the design); best matches first, falls back to all.</summary>
    public async Task<IReadOnlyList<int>> SuggestAsync(string? text, CancellationToken ct)
    {
        var cats = await db.Categories.Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync(ct);
        var t = text ?? "";
        var scored = cats.Select(c => (c.Id, Score: c.Keywords.Count(k => t.Contains(k, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).Select(x => x.Id).ToList();
        return scored.Count > 0 ? scored : cats.Select(c => c.Id).ToList();
    }

    public async Task<object> PublicConfigAsync(CancellationToken ct)
    {
        var r = await rules.GetAsync(ct);
        return new
        {
            r.MaxOffers, r.MaxEarlySlots, r.EarlyWindowSec, r.MaxOpenRequests, r.MaxVoiceSeconds, r.MinWordsWithoutMedia,
            r.ReplyWindowMin, r.GhostAfterHours, r.ExpandAfterMin, r.OtpResendSec, r.TrustedDeals, r.BillingEnabled,
            r.SelectionFee, r.FreeSelections, r.EarlyAccessMonthly, r.BannerDaily, r.FeaturedWeekly, r.SponsoredOffer,
            r.MinAdRating, r.MinTopUp, r.DisputeDecisionHours, r.MinAppVersion, r.TopUpMethods, r.RatingTags, r.LowRatingTags,
        };
    }

    public static BusinessRules Clone(BusinessRules r) =>
        JsonSerializer.Deserialize<BusinessRules>(JsonSerializer.Serialize(r))!;
}

using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

/// <summary>Home screen: real, anonymized aggregates only (no fake numbers).</summary>
public sealed class HomeService(Shared s, AdsService ads, RequestService requests)
{
    private IAppDbContext Db => s.Db;
    private static readonly OrderStatus[] Completed = [OrderStatus.Delivered, OrderStatus.Rated];

    /// <summary>Anonymized activity: category + governorate + offers count, delayed by FeedDelayMin.</summary>
    public async Task<IReadOnlyList<FeedItemDto>> FeedAsync(int? governorateId, int take, CancellationToken ct)
    {
        var rules = await s.Rules(ct);
        var before = s.Now.AddMinutes(-rules.FeedDelayMin);
        var q = Db.Requests.Include(r => r.Category).Include(r => r.Governorate).Where(r => r.CreatedAt <= before && r.OffersCount > 0);
        if (governorateId is { } g) q = q.Where(r => r.GovernorateId == g);
        return (await q.OrderByDescending(r => r.CreatedAt).Take(take).ToListAsync(ct))
            .Select(r => new FeedItemDto(r.Category!.Name, r.Governorate!.Name, r.OffersCount, s.Url(r.Category.ImageKey))).ToList();
    }

    public async Task<HomeDto> LoadAsync(int? governorateId, CancellationToken ct)
    {
        var rules = await s.Rules(ct);
        var now = s.Now;
        var take = rules.HomeListSize;
        var user = s.Me.UserId == Guid.Empty ? null : await Db.Users.FirstOrDefaultAsync(u => u.Id == s.Me.UserId, ct);
        var govId = governorateId ?? user?.GovernorateId ?? await Db.Governorates.OrderBy(g => g.SortOrder).Select(g => g.Id).FirstAsync(ct);
        var govName = await Db.Governorates.Where(g => g.Id == govId).Select(g => g.Name).FirstAsync(ct);

        var activePages = Db.Pages.Where(p => p.Status == PageStatus.Verified && (p.SuspendedUntil == null || p.SuspendedUntil < now));
        var localPages = await activePages.Where(p => p.Governorates.Any(g => g.GovernorateId == govId))
            .OrderByDescending(p => p.Deals).ThenByDescending(p => p.Rating).Take(take).ToListAsync(ct);

        var feedBefore = now.AddMinutes(-rules.FeedDelayMin);
        var feedRows = await Db.Requests.Include(r => r.Category).Include(r => r.Governorate)
            .Where(r => r.CreatedAt <= feedBefore && r.OffersCount > 0).OrderByDescending(r => r.CreatedAt).Take(take * 2).ToListAsync(ct);
        FeedItemDto Feed(Request r) => new(r.Category!.Name, r.Governorate!.Name, r.OffersCount, s.Url(r.Category.ImageKey));

        var verified = await activePages.CountAsync(ct);
        var govsServed = await Db.PageGovernorates.Where(g => activePages.Any(p => p.Id == g.PageId)).Select(g => g.GovernorateId).Distinct().CountAsync(ct);
        var firstOffers = await Db.Requests.Where(r => r.OffersCount > 0 && r.CreatedAt > now.AddDays(-7))
            .Select(r => new { r.CreatedAt, First = Db.Offers.Where(o => o.RequestId == r.Id).Min(o => o.CreatedAt) }).Take(500).ToListAsync(ct);
        var stats = new HomeStatsDto(
            verified >= rules.StatsMinVerifiedPages ? verified : null,
            govsServed >= rules.StatsMinGovernorates ? govsServed : null,
            firstOffers.Count >= rules.StatsMinSamples ? (int)firstOffers.Average(x => (x.First - x.CreatedAt).TotalMinutes) : null);

        var deals = await Db.Orders.Include(o => o.Offer).Include(o => o.Page).Include(o => o.Request).ThenInclude(r => r!.Category)
            .Where(o => Completed.Contains(o.Status) && o.AnsweredAt >= now.Date).OrderByDescending(o => o.AnsweredAt).Take(take).ToListAsync(ct);

        var categories = await Db.Categories.Where(c => c.IsActive).OrderBy(c => c.SortOrder).ToListAsync(ct);
        var live = user is null ? null : await Db.Requests.Where(r => r.CustomerId == user.Id &&
                (r.Status == RequestStatus.Open || r.Status == RequestStatus.Expanded || r.Status == RequestStatus.Locked))
            .OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
        var unread = user is not null && await Db.Notifications.AnyAsync(n => n.UserId == user.Id && n.ReadAt == null, ct);

        return new HomeDto(user?.Name?.Split(' ')[0], user?.Area, govName, unread,
            localPages.Select(p => new StoryDto(p.Id, p.Name, s.Url(p.ImageKey), false)).ToList(),
            live is null ? null : await requests.ToLiveAsync(live, ct),
            feedRows.Take(take).Select(Feed).ToList(), stats,
            categories.Select(c => new TrendingCategoryDto(c.Id, c.Name, s.Url(c.ImageKey), c.Badge.Code())).ToList(),
            feedRows.Where(r => r.GovernorateId == govId).Take(take).Select(Feed).ToList(),
            deals.Where(o => o.Request!.MaxPrice > o.Offer!.Price)
                .Select(o => new DealDto(o.Request!.Category!.Name, o.Page!.Name, o.Request.MaxPrice - o.Offer!.Price, s.Url(o.Request.Category.ImageKey))).ToList(),
            await ads.BannerAsync(govId, null, ct),
            localPages.Select(p => new PageSummaryDto(p.Id, p.Name, Math.Round(p.Rating, 1), p.Deals, s.Url(p.ImageKey), false)).ToList());
    }
}

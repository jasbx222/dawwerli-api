using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

/// <summary>
/// Customer home and pages tab: best-selling and highest-rated products, the top-5 featured pages strip,
/// the pages directory, a page's profile, and following pages.
/// </summary>
public sealed class DiscoveryService(StoreKit kit, Shared s)
{
    public const int TopPagesMax = 5;
    private const int PreviewImages = 3;

    private IAppDbContext Db => kit.Db;
    private Guid Me => s.Me.UserId;

    /// <summary>Store orders whose units count as sold (best-selling rankings).</summary>
    internal static readonly StoreOrderStatus[] SoldStatuses = [StoreOrderStatus.Accepted, StoreOrderStatus.OutForDelivery, StoreOrderStatus.Delivered];

    private IQueryable<Page> ActivePages
    {
        get
        {
            var now = s.Now;
            return Db.Pages.Where(p => p.Status == PageStatus.Verified && (p.SuspendedUntil == null || p.SuspendedUntil <= now));
        }
    }

    /// <summary>Visible products of active pages.</summary>
    private IQueryable<Product> Listed
    {
        get
        {
            var pages = ActivePages;
            return Db.Products.Where(p => p.Active && !p.HiddenByAdmin && p.DeletedAt == null && pages.Any(pg => pg.Id == p.PageId));
        }
    }

    // ---------- products ----------

    /// <summary>Ranked by units in accepted, on-the-way or delivered store orders; ties by rating, then newest.</summary>
    public async Task<PagedDto<DiscoverProductDto>> BestSellingAsync(int? pageNo, int? size, CancellationToken ct)
    {
        var (p, n) = kit.Paging(pageNo, size);
        var sold = Db.StoreOrders.Where(o => SoldStatuses.Contains(o.Status)).SelectMany(o => o.Items)
            .GroupBy(i => i.ProductId).Select(g => new { ProductId = g.Key, Qty = g.Sum(i => i.Quantity) });
        var q = Listed.Join(sold, x => x.Id, x => x.ProductId, (x, y) => new { P = x, y.Qty });
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.Qty).ThenByDescending(x => x.P.Rating).ThenByDescending(x => x.P.CreatedAt).ThenBy(x => x.P.Id)
            .Skip((p - 1) * n).Take(n).Select(x => new { x.P, PageName = x.P.Page!.Name, x.Qty }).ToListAsync(ct);
        return new PagedDto<DiscoverProductDto>(rows.Select(r => ToDto(r.P, r.PageName, r.Qty)).ToList(), total, p, n);
    }

    /// <summary>Products by their own average (at least one visible rating), then rating count.</summary>
    public async Task<PagedDto<DiscoverProductDto>> HighestRatedAsync(int? pageNo, int? size, CancellationToken ct)
    {
        var (p, n) = kit.Paging(pageNo, size);
        var q = Listed.Where(x => x.RatingCount > 0);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.Rating).ThenByDescending(x => x.RatingCount).ThenByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((p - 1) * n).Take(n).Select(x => new { P = x, PageName = x.Page!.Name }).ToListAsync(ct);
        var ids = rows.Select(r => r.P.Id).ToList();
        var sold = await SoldAsync(ids, ct);
        return new PagedDto<DiscoverProductDto>(rows.Select(r => ToDto(r.P, r.PageName, sold.GetValueOrDefault(r.P.Id))).ToList(), total, p, n);
    }

    private async Task<Dictionary<Guid, int>> SoldAsync(List<Guid> ids, CancellationToken ct) =>
        await Db.StoreOrders.Where(o => SoldStatuses.Contains(o.Status)).SelectMany(o => o.Items).Where(i => ids.Contains(i.ProductId))
            .GroupBy(i => i.ProductId).Select(g => new { g.Key, Qty = g.Sum(i => i.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

    private DiscoverProductDto ToDto(Product x, string pageName, int sold) => new(x.Id, x.PageId, pageName, x.Name, x.Description, x.Price,
        s.Url(x.ImageKeys.FirstOrDefault()), x.Stock, x.Available, x.Rating, x.RatingCount, sold);

    // ---------- pages ----------

    /// <summary>The home strip: admin-picked slots first (by slot), then the best-rated active pages, at most five.</summary>
    public async Task<IReadOnlyList<PageCardDto>> TopPagesAsync(int? take, CancellationToken ct)
    {
        var n = Math.Clamp(take ?? TopPagesMax, 1, TopPagesMax);
        var picked = await ActivePages.Where(p => p.FeaturedOrder != null).OrderBy(p => p.FeaturedOrder).Take(n).ToListAsync(ct);
        var ids = picked.Select(p => p.Id).ToList();
        if (picked.Count < n)
            picked.AddRange(await ActivePages.Where(p => !ids.Contains(p.Id))
                .OrderByDescending(p => p.Rating).ThenByDescending(p => p.RatingCount).ThenByDescending(p => p.Deals).ThenBy(p => p.Id)
                .Take(n - picked.Count).ToListAsync(ct));
        return await CardsAsync(picked, ct);
    }

    /// <summary>All active pages for the pages tab: search by name, filter by category, or only the ones I follow.</summary>
    public async Task<PagedDto<PageCardDto>> DirectoryAsync(string? search, int? categoryId, bool following, int? pageNo, int? size, CancellationToken ct)
    {
        var (p, n) = kit.Paging(pageNo, size);
        var q = ActivePages;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim().ToLower()}%";
            q = q.Where(x => EF.Functions.Like(x.Name.ToLower(), term) || EF.Functions.Like(x.InstagramHandle.ToLower(), term));
        }
        if (categoryId is { } cat) q = q.Where(x => x.Categories.Any(c => c.CategoryId == cat));
        if (following)
        {
            var me = Me;
            q = q.Where(x => Db.PageFollows.Any(f => f.PageId == x.Id && f.UserId == me));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderBy(x => x.FeaturedOrder == null).ThenBy(x => x.FeaturedOrder)
            .ThenByDescending(x => x.Rating).ThenByDescending(x => x.RatingCount).ThenByDescending(x => x.Deals).ThenBy(x => x.Id)
            .Skip((p - 1) * n).Take(n).ToListAsync(ct);
        return new PagedDto<PageCardDto>(await CardsAsync(rows, ct), total, p, n);
    }

    private async Task<IReadOnlyList<PageCardDto>> CardsAsync(List<Page> pages, CancellationToken ct)
    {
        if (pages.Count == 0) return [];
        var ids = pages.Select(p => p.Id).ToList();
        var me = Me;
        var rules = await s.Rules(ct);
        var followers = await Db.PageFollows.Where(f => ids.Contains(f.PageId)).GroupBy(f => f.PageId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var mine = me == Guid.Empty ? [] : await Db.PageFollows.Where(f => f.UserId == me && ids.Contains(f.PageId)).Select(f => f.PageId).ToListAsync(ct);
        var products = await Db.Products.Where(p => ids.Contains(p.PageId) && p.Active && !p.HiddenByAdmin && p.DeletedAt == null)
            .OrderByDescending(p => p.CreatedAt).Select(p => new { p.PageId, p.ImageKeys }).ToListAsync(ct);
        var byPage = products.GroupBy(p => p.PageId).ToDictionary(g => g.Key, g => g.ToList());
        var catIds = await Db.PageCategories.Where(c => ids.Contains(c.PageId)).Select(c => new { c.PageId, c.CategoryId }).ToListAsync(ct);
        var allCats = catIds.Select(c => c.CategoryId).Distinct().ToList();
        var names = await Db.Categories.Where(c => allCats.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        return pages.Select(p =>
        {
            var own = byPage.GetValueOrDefault(p.Id, []);
            return new PageCardDto(p.Id, p.Name, s.Url(p.ImageKey), p.Bio, Math.Round(p.Rating, 1), p.RatingCount, p.Deals, p.Deals >= rules.TrustedDeals,
                own.Count, followers.GetValueOrDefault(p.Id), mine.Contains(p.Id),
                catIds.Where(c => c.PageId == p.Id).Select(c => names.GetValueOrDefault(c.CategoryId, "")).Where(x => x != "").ToList(),
                own.Where(x => x.ImageKeys.Length > 0).Take(PreviewImages).Select(x => s.Url(x.ImageKeys[0])).ToList(),
                p.FeaturedOrder);
        }).ToList();
    }

    /// <summary>Instagram-style header data: page info plus products, followers and completed orders.</summary>
    public async Task<Result<PageProfileDto>> ProfileAsync(Guid id, CancellationToken ct)
    {
        var p = await ActivePages.Include(x => x.Categories).Include(x => x.Governorates).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return Error.NotFound("page");
        var rules = await s.Rules(ct);
        var catIds = p.Categories.Select(c => c.CategoryId).ToList();
        var govIds = p.Governorates.Select(g => g.GovernorateId).ToList();
        var me = Me;
        var products = await Db.Products.CountAsync(x => x.PageId == id && x.Active && !x.HiddenByAdmin && x.DeletedAt == null, ct);
        var followers = await Db.PageFollows.CountAsync(f => f.PageId == id, ct);
        var delivered = await Db.StoreOrders.CountAsync(o => o.PageId == id && o.Status == StoreOrderStatus.Delivered, ct);
        var following = me != Guid.Empty && await Db.PageFollows.AnyAsync(f => f.PageId == id && f.UserId == me, ct);
        return new PageProfileDto(p.Id, p.Name, s.Url(p.ImageKey), p.Bio, Math.Round(p.Rating, 1), p.RatingCount, p.Deals, p.Deals >= rules.TrustedDeals,
            await Db.Categories.Where(c => catIds.Contains(c.Id)).Select(c => c.Name).ToListAsync(ct),
            await Db.Governorates.Where(g => govIds.Contains(g.Id)).OrderBy(g => g.SortOrder).Select(g => g.Name).ToListAsync(ct),
            p.CreatedAt.Year, products, followers, p.Deals + delivered, following);
    }

    // ---------- follows ----------

    /// <summary>Idempotent: following twice keeps one row.</summary>
    public async Task<Result<FollowStateDto>> FollowAsync(Guid pageId, CancellationToken ct)
    {
        if (!await ActivePages.AnyAsync(p => p.Id == pageId, ct)) return Error.NotFound("page");
        if (!await Db.PageFollows.AnyAsync(f => f.UserId == Me && f.PageId == pageId, ct))
        {
            Db.PageFollows.Add(new PageFollow { UserId = Me, PageId = pageId });
            try { await Db.SaveChangesAsync(ct); }
            catch (DbUpdateException) { /* a parallel tap won the unique index; same outcome */ }
        }
        return await StateAsync(pageId, ct);
    }

    public async Task<Result<FollowStateDto>> UnfollowAsync(Guid pageId, CancellationToken ct)
    {
        await Db.PageFollows.Where(f => f.UserId == Me && f.PageId == pageId).ExecuteDeleteAsync(ct);
        return await StateAsync(pageId, ct);
    }

    private async Task<FollowStateDto> StateAsync(Guid pageId, CancellationToken ct) => new(pageId,
        await Db.PageFollows.AnyAsync(f => f.UserId == Me && f.PageId == pageId, ct),
        await Db.PageFollows.CountAsync(f => f.PageId == pageId, ct));
}

/// <summary>
/// Tells a page's followers about new products. Throttled per follower: the first product in a 30-minute window
/// sends a notification and a push; later ones in the same window rewrite that notification into a summary, without a new push.
/// </summary>
public sealed class FollowerNotifier(IAppDbContext db, IClock clock, IPushSender push, ITextCatalog texts)
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    public async Task NewProductAsync(Page page, Product product, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var follows = await db.PageFollows.Where(f => f.PageId == page.Id).ToListAsync(ct);
        if (follows.Count == 0) return;
        var openIds = follows.Where(f => f.LastNotificationId != null && f.LastNotifiedAt > now - Window).Select(f => f.LastNotificationId!.Value).ToList();
        var open = await db.Notifications.Where(n => openIds.Contains(n.Id)).ToDictionaryAsync(n => n.Id, ct);
        var fresh = new List<Guid>();
        var title = texts.Get("follow.newProduct.title", page.Name);
        foreach (var f in follows)
        {
            if (f.LastNotificationId is { } nid && f.LastNotifiedAt > now - Window && open.TryGetValue(nid, out var note))
            {
                f.WindowCount++;
                note.Title = title;
                note.Body = texts.Get("follow.newProducts.body", page.Name, f.WindowCount);
                note.Route = $"/pages/{page.Id}";
                note.ReadAt = null;
                continue;
            }
            var n = new Notification { UserId = f.UserId, Title = title, Body = texts.Get("follow.newProduct.body", product.Name), Route = $"/products/{product.Id}" };
            db.Notifications.Add(n);
            f.LastNotificationId = n.Id;
            f.LastNotifiedAt = now;
            f.WindowCount = 1;
            fresh.Add(f.UserId);
        }
        await db.SaveChangesAsync(ct);
        if (fresh.Count == 0) return;
        var tokens = await db.Devices.Where(d => fresh.Contains(d.UserId)).Select(d => d.PushToken).ToListAsync(ct);
        if (tokens.Count > 0) await push.SendAsync(tokens, title, texts.Get("follow.newProduct.body", product.Name), $"/products/{product.Id}", ct);
    }
}

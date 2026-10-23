using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

/// <summary>Customer side of the page store: browse a page's products, one-page cart, checkout and store orders.</summary>
public sealed class StoreService(StoreKit kit, Shared s, IPaymentProvider payments, INotifier notifier, ITextCatalog texts, OfferOrderLink link)
{
    private IAppDbContext Db => kit.Db;
    private Guid Me => s.Me.UserId;

    private IQueryable<Product> Visible => Db.Products.Where(p => p.Active && !p.HiddenByAdmin && p.DeletedAt == null);

    // ---------- browse ----------

    public async Task<Result<PagedDto<ProductCardDto>>> PageProductsAsync(Guid pageId, int? pageNo, int? size, CancellationToken ct)
    {
        var page = await Db.Pages.FirstOrDefaultAsync(p => p.Id == pageId, ct);
        if (page is null || !page.IsActive(s.Now)) return Error.NotFound("page");
        var (p, n) = kit.Paging(pageNo, size);
        var q = Visible.Where(x => x.PageId == pageId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.Stock > 0).ThenByDescending(x => x.CreatedAt).Skip((p - 1) * n).Take(n).ToListAsync(ct);
        return new PagedDto<ProductCardDto>(rows.Select(kit.ToCard).ToList(), total, p, n);
    }

    /// <summary>
    /// Home strip: in-stock, visible products of the highest-rated active pages (rating, then rating count, then deals).
    /// Products keep the page order; each page contributes its newest products.
    /// </summary>
    public async Task<IReadOnlyList<TopRatedProductDto>> TopRatedProductsAsync(int? pages, int? perPage, CancellationToken ct)
    {
        var o = kit.Options;
        var pageCount = Math.Clamp(pages ?? o.TopRatedPages, 1, o.MaxPageSize);
        var each = Math.Clamp(perPage ?? o.TopRatedProductsPerPage, 1, o.MaxPageSize);
        var now = s.Now;
        var inStock = Visible.Where(p => p.Stock > 0);
        var top = await Db.Pages
            .Where(pg => pg.Status == PageStatus.Verified && (pg.SuspendedUntil == null || pg.SuspendedUntil <= now))
            .Where(pg => inStock.Any(p => p.PageId == pg.Id))
            .OrderByDescending(pg => pg.Rating).ThenByDescending(pg => pg.RatingCount).ThenByDescending(pg => pg.Deals).ThenBy(pg => pg.Id)
            .Take(pageCount)
            .Select(pg => new { pg.Id, pg.Name, pg.Rating, pg.RatingCount })
            .ToListAsync(ct);
        var ids = top.Select(t => t.Id).ToList();
        var rows = await inStock.Where(p => ids.Contains(p.PageId)).OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
        var byPage = rows.GroupBy(p => p.PageId).ToDictionary(g => g.Key, g => g.Take(each).ToList());
        return top.SelectMany(pg => byPage.GetValueOrDefault(pg.Id, []).Select(p => new TopRatedProductDto(p.Id, pg.Id, pg.Name, pg.Rating, pg.RatingCount,
            p.Name, p.Description, p.Price, s.Url(p.ImageKeys.FirstOrDefault()), p.Stock, p.Available, p.Rating, p.RatingCount))).ToList();
    }

    public async Task<Result<ProductDetailsDto>> ProductAsync(Guid id, CancellationToken ct)
    {
        var x = await Visible.Include(p => p.Page).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (x?.Page is null || !x.Page.IsActive(s.Now)) return StoreErrors.ProductNotFound;
        ProductReviewDto? mine = null;
        var favorite = false;
        if (Me != Guid.Empty)
        {
            mine = await Db.ProductRatings.Where(r => r.ProductId == id && r.UserId == Me)
                .Select(r => new ProductReviewDto(r.Id, r.User!.Name ?? "", r.Stars, r.Comment, r.UpdatedAt)).FirstOrDefaultAsync(ct);
            if (mine is not null) mine = mine with { CustomerName = FirstName(mine.CustomerName) };
            favorite = await Db.ProductFavorites.AnyAsync(f => f.ProductId == id && f.UserId == Me, ct);
        }
        var variants = await VariantsAsync(x, ct);
        return new ProductDetailsDto(x.Id, x.PageId, x.Page.Name, x.Name, x.Description, x.Price, x.ImageKeys.Select(s.Url).ToList(),
            x.CategoryId, x.Stock, x.Available, x.Rating, x.RatingCount, mine is not null, mine, favorite,
            x.WidthCm, x.HeightCm, x.DepthCm, StoreKit.WallKindCode(x), variants.Count > 0 ? x.VariantLabel : null, variants);
    }

    /// <summary>Visible siblings from the same page and group only; a lone product gets an empty list.</summary>
    private async Task<IReadOnlyList<ProductVariantDto>> VariantsAsync(Product x, CancellationToken ct)
    {
        if (x.VariantGroup is null) return [];
        var rows = await Visible.Where(p => p.PageId == x.PageId && p.VariantGroup == x.VariantGroup)
            .OrderBy(p => p.WidthCm).ThenBy(p => p.Price).ThenBy(p => p.CreatedAt)
            .Take(kit.Options.MaxVariantsPerGroup).ToListAsync(ct);
        if (rows.Count < 2) return [];
        return rows.Select(p => new ProductVariantDto(p.Id, p.VariantLabel ?? p.Name, p.Name, p.Price, p.Stock, p.Available,
            s.Url(p.ImageKeys.FirstOrDefault()), p.WidthCm, p.HeightCm, p.DepthCm, StoreKit.WallKindCode(p))).ToList();
    }

    public async Task<Result> ReportAsync(Guid productId, ReportProductDto dto, CancellationToken ct)
    {
        if (!EnumText.TryParse<ReportReason>(dto.Reason, out var reason)) return Errors.BadEnum("reason");
        if (!await Db.Products.AnyAsync(p => p.Id == productId && p.DeletedAt == null, ct)) return StoreErrors.ProductNotFound;
        if (await Db.Reports.AnyAsync(r => r.ReporterUserId == Me && r.Target == ReportTarget.Product && r.TargetId == productId && r.Status == CaseStatus.Open, ct))
            return Error.Conflict("report.duplicate", "Already reported");
        Db.Reports.Add(new Report { ReporterUserId = Me, Target = ReportTarget.Product, TargetId = productId, Reason = reason, Text = dto.Text?.Trim() ?? "" });
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    // ---------- favorites ----------

    public async Task<IReadOnlyList<FavoriteProductDto>> FavoritesAsync(CancellationToken ct)
    {
        var now = s.Now;
        var rows = await Db.ProductFavorites.Include(f => f.Product).ThenInclude(p => p!.Page)
            .Where(f => f.UserId == Me && f.Product!.DeletedAt == null)
            .OrderByDescending(f => f.CreatedAt).ToListAsync(ct);
        return rows.Select(f =>
        {
            var p = f.Product!;
            var available = p.Available && p.Page is not null && p.Page.IsActive(now);
            return new FavoriteProductDto(p.Id, p.PageId, p.Page?.Name ?? "", p.Name, p.Description, p.Price, s.Url(p.ImageKeys.FirstOrDefault()),
                p.Stock, available, p.Rating, p.RatingCount, f.CreatedAt);
        }).ToList();
    }

    /// <summary>Idempotent: hearting twice keeps one row.</summary>
    public async Task<Result> AddFavoriteAsync(Guid productId, CancellationToken ct)
    {
        if (!await Visible.AnyAsync(p => p.Id == productId, ct)) return StoreErrors.ProductNotFound;
        if (await Db.ProductFavorites.AnyAsync(f => f.UserId == Me && f.ProductId == productId, ct)) return Result.Ok();
        Db.ProductFavorites.Add(new ProductFavorite { UserId = Me, ProductId = productId });
        try { await Db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { /* a parallel tap won the unique index; same outcome */ }
        return Result.Ok();
    }

    public async Task RemoveFavoriteAsync(Guid productId, CancellationToken ct) =>
        await Db.ProductFavorites.Where(f => f.UserId == Me && f.ProductId == productId).ExecuteDeleteAsync(ct);

    // ---------- ratings ----------

    /// <summary>Only customers who received the product (a delivered store order containing it) can rate it.</summary>
    private Task<bool> ReceivedAsync(Guid productId, CancellationToken ct) =>
        Db.StoreOrders.AnyAsync(o => o.CustomerUserId == Me && o.Status == StoreOrderStatus.Delivered && o.Items.Any(i => i.ProductId == productId), ct);

    public async Task<Result<PagedDto<ProductReviewDto>>> ReviewsAsync(Guid productId, int? pageNo, int? size, CancellationToken ct)
    {
        if (!await Visible.AnyAsync(p => p.Id == productId, ct)) return StoreErrors.ProductNotFound;
        var (p, n) = kit.Paging(pageNo, size);
        var q = Db.ProductRatings.Where(r => r.ProductId == productId && !r.HiddenByAdmin);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.UpdatedAt).Skip((p - 1) * n).Take(n)
            .Select(r => new { r.Id, r.User!.Name, r.Stars, r.Comment, r.UpdatedAt }).ToListAsync(ct);
        return new PagedDto<ProductReviewDto>(rows.Select(r => new ProductReviewDto(r.Id, FirstName(r.Name), r.Stars, r.Comment, r.UpdatedAt)).ToList(), total, p, n);
    }

    public async Task<Result<MyProductRatingDto>> MyRatingAsync(Guid productId, CancellationToken ct)
    {
        if (!await Db.Products.AnyAsync(p => p.Id == productId && p.DeletedAt == null, ct)) return StoreErrors.ProductNotFound;
        var mine = await Db.ProductRatings.FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == Me, ct);
        var canRate = await Visible.AnyAsync(p => p.Id == productId, ct) && await ReceivedAsync(productId, ct);
        return new MyProductRatingDto(canRate, mine?.Stars, mine?.Comment);
    }

    public static readonly Error DuplicateRating = Error.Conflict("rating.duplicate", "قيّمت هذا المنتج من قبل، تكدر تعدّل تقييمك بس");

    /// <summary>A new rating only: one per customer per product. A second attempt is a 409; the owner edits through <see cref="RateAsync"/>.</summary>
    public async Task<Result<ProductRatingSummaryDto>> CreateRatingAsync(Guid productId, RateProductDto dto, CancellationToken ct)
    {
        if (await Db.ProductRatings.AnyAsync(r => r.ProductId == productId && r.UserId == Me, ct)) return DuplicateRating;
        return await RateAsync(productId, dto, ct, createOnly: true);
    }

    /// <summary>Creates or edits the customer's rating, then refreshes the product's average and count.</summary>
    public async Task<Result<ProductRatingSummaryDto>> RateAsync(Guid productId, RateProductDto dto, CancellationToken ct, bool createOnly = false)
    {
        if (dto.Stars is < 1 or > 5) return Error.Validation("rating.stars", "Stars must be between 1 and 5");
        var comment = dto.Comment?.Trim() ?? "";
        if (comment.Length > kit.Options.RatingCommentMaxLength)
            return new Error(ErrorKind.Validation, "rating.comment", "Comment too long", new Dictionary<string, object?> { ["max"] = kit.Options.RatingCommentMaxLength });
        if (await kit.BannedWordAsync(ct, comment) is { } word)
            return new Error(ErrorKind.BusinessRule, "rating.prohibited", "Text contains a banned word", new Dictionary<string, object?> { ["word"] = word });
        if (!await Visible.AnyAsync(p => p.Id == productId, ct)) return StoreErrors.ProductNotFound;
        if (!await ReceivedAsync(productId, ct)) return Error.Rule("rating.not_received", "You can rate a product after you receive it");

        var mine = await Db.ProductRatings.FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == Me, ct);
        if (createOnly && mine is not null) return DuplicateRating;
        if (mine is null)
        {
            Db.ProductRatings.Add(mine = new ProductRating { ProductId = productId, UserId = Me, Stars = dto.Stars, Comment = comment });
            try { await Db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                // A parallel request from the same customer inserted first: update that row instead.
                Db.Entry(mine).State = EntityState.Detached;
                if (createOnly) return DuplicateRating;
                mine = await Db.ProductRatings.FirstAsync(r => r.ProductId == productId && r.UserId == Me, ct);
            }
        }
        mine.Stars = dto.Stars;
        mine.Comment = comment;
        await Db.SaveChangesAsync(ct);

        // One statement, so parallel ratings of the same product never write a stale average or count.
        await Db.RefreshProductRatingAsync(productId, ct);
        var after = await Db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => new { p.Rating, p.RatingCount }).FirstAsync(ct);
        return new ProductRatingSummaryDto(after.Rating, after.RatingCount, mine.Stars, mine.Comment);
    }

    private static string FirstName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "زبون" : name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

    // ---------- cart ----------

    public async Task<CartDto> CartAsync(CancellationToken ct)
    {
        var items = await Db.CartItems.Include(i => i.Product).ThenInclude(p => p!.Page).Where(i => i.UserId == Me).OrderBy(i => i.CreatedAt).ToListAsync(ct);
        var lines = items.Where(i => i.Product is not null).Select(i => new CartLineDto(i.ProductId, i.Product!.Name, s.Url(i.Product.ImageKeys.FirstOrDefault()),
            i.Product.Price, i.Quantity, i.Product.Price * i.Quantity, i.Product.Available && i.Product.Stock >= i.Quantity, i.Product.Stock)).ToList();
        var first = items.FirstOrDefault()?.Product?.Page;
        return new CartDto(first?.Id, first?.Name, lines, lines.Sum(l => l.LineTotal));
    }

    public async Task<Result<CartDto>> AddAsync(AddCartItemDto dto, CancellationToken ct)
    {
        var product = await Visible.Include(p => p.Page).FirstOrDefaultAsync(p => p.Id == dto.ProductId, ct);
        if (product?.Page is null || !product.Page.IsActive(s.Now)) return StoreErrors.ProductNotFound;
        if (!product.Available) return StoreErrors.Unavailable;
        if (await Db.CartItems.AnyAsync(i => i.UserId == Me && i.PageId != product.PageId, ct)) return StoreErrors.CartOtherPage;
        var line = await Db.CartItems.FirstOrDefaultAsync(i => i.UserId == Me && i.ProductId == product.Id, ct);
        var qty = (line?.Quantity ?? 0) + dto.Quantity;
        if (QuantityError(product, qty) is { } bad) return bad;
        if (line is null) Db.CartItems.Add(new CartItem { UserId = Me, PageId = product.PageId, ProductId = product.Id, Quantity = qty });
        else line.Quantity = qty;
        await Db.SaveChangesAsync(ct);
        return await CartAsync(ct);
    }

    public async Task<Result<CartDto>> SetQuantityAsync(Guid productId, SetCartQuantityDto dto, CancellationToken ct)
    {
        var line = await Db.CartItems.Include(i => i.Product).FirstOrDefaultAsync(i => i.UserId == Me && i.ProductId == productId, ct);
        if (line?.Product is null) return Error.NotFound("cart_item");
        if (dto.Quantity <= 0) Db.CartItems.Remove(line);
        else
        {
            if (QuantityError(line.Product, dto.Quantity) is { } bad) return bad;
            line.Quantity = dto.Quantity;
        }
        await Db.SaveChangesAsync(ct);
        return await CartAsync(ct);
    }

    public async Task<CartDto> RemoveAsync(Guid productId, CancellationToken ct)
    {
        await Db.CartItems.Where(i => i.UserId == Me && i.ProductId == productId).ExecuteDeleteAsync(ct);
        return await CartAsync(ct);
    }

    public async Task ClearAsync(CancellationToken ct) => await Db.CartItems.Where(i => i.UserId == Me).ExecuteDeleteAsync(ct);

    private Error? QuantityError(Product p, int qty)
    {
        if (qty > kit.Options.MaxCartQuantity)
            return new Error(ErrorKind.Validation, "cart.quantity", "Quantity too large", new Dictionary<string, object?> { ["max"] = kit.Options.MaxCartQuantity });
        return qty > p.Stock ? StoreErrors.InsufficientStock(p.Id, p.Stock) : null;
    }

    // ---------- checkout ----------

    public async Task<Result<CheckoutResultDto>> CheckoutAsync(CheckoutDto dto, CancellationToken ct)
    {
        if (!EnumText.TryParse<PaymentMethod>(dto.PaymentMethod, out var method)) return Errors.BadEnum("paymentMethod");
        if (OrderPin.Check(dto.Latitude, dto.Longitude, dto.Note, dto.LocationNote) is { } pinError) return pinError;
        if (method == PaymentMethod.Electronic && !payments.IsConfigured) return StoreErrors.PaymentNotConfigured;
        var user = await Db.Users.FirstAsync(u => u.Id == Me, ct);
        if (user.IsBanned) return Error.Forbidden("account.banned", "Account is banned");

        await using var tx = await Db.BeginTransactionAsync(ct);
        var items = await Db.CartItems.Include(i => i.Product).ThenInclude(p => p!.Page).Where(i => i.UserId == Me).ToListAsync(ct);
        if (items.Count == 0) return StoreErrors.CartEmpty;
        // Claim the cart: a parallel checkout of the same cart waits on these rows, then finds nothing to delete.
        var claimed = await Db.CartItems.Where(i => i.UserId == Me).ExecuteDeleteAsync(ct);
        if (claimed != items.Count) return StoreErrors.CartEmpty;
        if (items.Select(i => i.PageId).Concat(items.Select(i => i.Product!.PageId)).Distinct().Count() != 1) return StoreErrors.CartOtherPage;
        var page = items[0].Product!.Page!;
        if (!page.IsActive(s.Now)) return Error.Rule("page.unavailable", "This page is not taking orders right now");
        if (items.FirstOrDefault(i => !i.Product!.Visible) is { } gone) return new Error(ErrorKind.Conflict, StoreErrors.Unavailable.Code, "Product is not available",
            new Dictionary<string, object?> { ["productId"] = gone.ProductId });

        foreach (var i in items)
            if (!await kit.ReserveAsync(i.ProductId, i.Quantity, ct))
            {
                await tx.RollbackAsync(ct);
                var stock = await Db.Products.Where(p => p.Id == i.ProductId).Select(p => p.Stock).FirstAsync(ct);
                return StoreErrors.InsufficientStock(i.ProductId, stock);
            }

        var rules = await s.Rules(ct);
        var now = s.Now;
        var order = new StoreOrder
        {
            Number = await kit.NextNumberAsync(ct), CustomerUserId = Me, PageId = page.Id, Status = StoreOrderStatus.New,
            PaymentMethod = method, PaymentStatus = method == PaymentMethod.Cash ? PaymentStatus.CashOnDelivery : PaymentStatus.Pending,
            Address = dto.Address.Trim(), GovernorateId = dto.GovernorateId, Note = dto.Note?.Trim(), StatusChangedAt = now,
            Latitude = dto.Latitude, Longitude = dto.Longitude, LocationNote = OrderPin.Clean(dto.LocationNote),
            CommissionPercent = rules.StoreCommissionPercent,
            Items = items.Select(i => new StoreOrderItem { ProductId = i.ProductId, Name = i.Product!.Name, UnitPrice = i.Product.Price,
                Quantity = i.Quantity, ImageKey = i.Product.ImageKeys.FirstOrDefault() }).ToList(),
        };
        order.History.Add(new StoreOrderEvent { OrderId = order.Id, Status = StoreOrderStatus.New, At = now });
        order.Subtotal = order.Items.Sum(i => i.UnitPrice * i.Quantity);
        order.Commission = StoreOrderFlow.CommissionOf(order.Subtotal, order.CommissionPercent);
        Db.StoreOrders.Add(order);
        foreach (var i in items) Db.Entry(i).State = EntityState.Detached;

        string? payUrl = null;
        if (method == PaymentMethod.Electronic)
        {
            var session = await payments.StartAsync(order, order.Subtotal, ct);
            order.PaymentReference = session.Reference;
            payUrl = session.CheckoutUrl;
        }
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await notifier.NotifyAsync(page.UserId, texts.Get("store.newOrder.title"),
            texts.Get("store.newOrder.body", Shared.OrderNo(order.Number), order.Subtotal), $"/seller/store/orders/{order.Id}", ct);
        return new CheckoutResultDto(order.Id, Shared.OrderNo(order.Number), order.Status.Code(), order.PaymentStatus.Code(), order.Subtotal, payUrl);
    }

    // ---------- my store orders ----------

    public async Task<PagedDto<StoreOrderDto>> MyOrdersAsync(int? pageNo, int? size, CancellationToken ct)
    {
        var (p, n) = kit.Paging(pageNo, size);
        var q = kit.Orders.Where(o => o.CustomerUserId == Me);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(o => o.CreatedAt).Skip((p - 1) * n).Take(n).ToListAsync(ct);
        var deals = await kit.DealsAsync(rows, ct);
        return new PagedDto<StoreOrderDto>(rows.Select(o => kit.ToDto(o, deals)).ToList(), total, p, n);
    }

    public async Task<Result<StoreOrderDto>> MyOrderAsync(Guid id, CancellationToken ct)
    {
        var o = await kit.Orders.FirstOrDefaultAsync(x => x.Id == id && x.CustomerUserId == Me, ct);
        if (o is null) return StoreErrors.OrderNotFound;
        return kit.ToDto(o, await kit.DealsAsync([o], ct));
    }

    public async Task<Result<StoreOrderDto>> CancelAsync(Guid id, CancellationToken ct)
    {
        await using var tx = await Db.BeginTransactionAsync(ct);
        // Serializes against the seller's status moves: only one of them ends the order and restores stock.
        await Db.LockStoreOrderAsync(id, ct);
        var o = await kit.Orders.FirstOrDefaultAsync(x => x.Id == id && x.CustomerUserId == Me, ct);
        if (o is null) return StoreErrors.OrderNotFound;
        if (!StoreOrderFlow.CustomerCanCancel(o.Status)) return StoreErrors.BadTransition;
        if (o.Kind == StoreOrderKind.Offer && await link.OnCustomerCancelAsync(o, ct) is { } dealError) return dealError;
        o.Record(StoreOrderStatus.Cancelled, s.Now);
        if (o.Kind == StoreOrderKind.Store) await kit.RestoreStockAsync(o, ct);
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.NotifyAsync(o.Page!.UserId, texts.Get("store.customerCancelled.title"),
            texts.Get("store.customerCancelled.body", Shared.OrderNo(o.Number)), $"/seller/store/orders/{o.Id}", ct);
        return kit.ToDto(o, await kit.DealsAsync([o], ct));
    }
}

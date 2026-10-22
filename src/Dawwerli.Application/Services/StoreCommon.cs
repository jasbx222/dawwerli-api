using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dawwerli.Application.Services;

public static class StoreErrors
{
    public static readonly Error ProductNotFound = Error.NotFound("product");
    public static readonly Error OrderNotFound = Error.NotFound("store_order");
    public static readonly Error CartOtherPage = Error.Conflict("cart.other_page", "Cart already has products from another page");
    public static readonly Error CartEmpty = Error.Rule("cart.empty", "Cart is empty");
    public static readonly Error Unavailable = Error.Rule("product.unavailable", "Product is not available");
    public static readonly Error PaymentNotConfigured = Error.Rule("payment.not_configured", "Electronic payment is not available yet");
    public static readonly Error BadTransition = Error.Conflict("store_order.transition", "Status change not allowed");
    public static Error LimitReached(int limit) => new(ErrorKind.BusinessRule, "product.limit", "Product limit reached",
        new Dictionary<string, object?> { ["limit"] = limit });
    public static Error InsufficientStock(Guid productId, int stock) => new(ErrorKind.Conflict, "product.stock", "Not enough stock",
        new Dictionary<string, object?> { ["productId"] = productId, ["stock"] = stock });
}

/// <summary>Store lookups, limits, stock and mapping shared by the seller, customer and admin store services.</summary>
public sealed class StoreKit(Shared s, IOptions<StoreOptions> options)
{
    public StoreOptions Options => options.Value;
    public IAppDbContext Db => s.Db;

    public async Task<int> ProductLimitAsync(Page page, CancellationToken ct)
    {
        if (page.MaxProducts is { } own) return own;
        var global = (await s.Rules(ct)).MaxProductsPerPage;
        return global > 0 ? global : Options.DefaultMaxProductsPerPage;
    }

    public Task<int> ProductCountAsync(Guid pageId, CancellationToken ct) =>
        Db.Products.CountAsync(p => p.PageId == pageId && p.DeletedAt == null, ct);

    /// <summary>Deepest page served; keeps (page - 1) * size far from int overflow.</summary>
    public const int MaxPageNumber = 10_000;

    public (int Page, int Size) Paging(int? page, int? size) =>
        (Math.Clamp(page ?? 1, 1, MaxPageNumber), Math.Clamp(size ?? Options.DefaultPageSize, 1, Options.MaxPageSize));

    /// <summary>Reserves stock atomically (no oversell under concurrency). Returns false when stock is short.</summary>
    public async Task<bool> ReserveAsync(Guid productId, int qty, CancellationToken ct) =>
        await Db.Products.Where(p => p.Id == productId && p.Stock >= qty)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.Stock, p => p.Stock - qty), ct) == 1;

    public async Task RestoreStockAsync(StoreOrder order, CancellationToken ct)
    {
        foreach (var i in order.Items)
            await Db.Products.Where(p => p.Id == i.ProductId).ExecuteUpdateAsync(x => x.SetProperty(p => p.Stock, p => p.Stock + i.Quantity), ct);
    }

    public async Task<string?> BannedWordAsync(CancellationToken ct, params string?[] texts)
    {
        var banned = await Db.BannedKeywords.Select(b => b.Word).ToListAsync(ct);
        return texts.Select(t => ContactFilter.FindBanned(t, banned)).FirstOrDefault(w => w is not null);
    }

    public IQueryable<StoreOrder> Orders => Db.StoreOrders.Include(o => o.Items).Include(o => o.History).Include(o => o.Page).AsSplitQuery();

    public StoreOrderDto ToDto(StoreOrder o) => new(o.Id, Shared.OrderNo(o.Number), o.PageId, o.Page?.Name ?? "", o.Status.Code(),
        o.PaymentMethod.Code(), o.PaymentStatus.Code(), o.Address, o.GovernorateId, o.Note, o.Subtotal, o.Commission, o.StatusReason,
        o.Items.Select(i => new StoreOrderItemDto(i.ProductId, i.Name, i.UnitPrice, i.Quantity, s.Url(i.ImageKey))).ToList(), o.CreatedAt, o.StatusChangedAt,
        o.History.OrderBy(e => e.At).Select(e => new StoreOrderEventDto(e.Status.Code(), e.At)).ToList());

    public SellerProductDto ToSellerDto(Product p) => new(p.Id, p.Name, p.Description, p.Price, p.ImageKeys, p.ImageKeys.Select(s.Url).ToList(),
        p.CategoryId, p.Stock, p.Active, p.HiddenByAdmin, p.Available, p.CreatedAt, p.Rating, p.RatingCount);

    public ProductCardDto ToCard(Product p) => new(p.Id, p.Name, p.Price, s.Url(p.ImageKeys.FirstOrDefault()), p.Available,
        p.Description, p.Stock, p.Rating, p.RatingCount);

    /// <summary>Store orders reuse the request/order "DW-" number sequence.</summary>
    public Task<long> NextNumberAsync(CancellationToken ct) => Db.NextNumberAsync(OrderSequence, ct);
    public const string OrderSequence = "order_number";
}

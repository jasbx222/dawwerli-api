using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

/// <summary>Prepaid balance over an append-only ledger. Balance on Page is the cached sum.</summary>
public sealed class WalletService(Shared s, ITextCatalog texts)
{
    private IAppDbContext Db => s.Db;

    /// <summary>Balance minus fees reserved by this page's open offers.</summary>
    public async Task<long> AvailableAsync(Page page, BusinessRules rules, CancellationToken ct)
    {
        if (!rules.BillingEnabled) return long.MaxValue;
        var open = await Db.Offers.CountAsync(o => o.PageId == page.Id && o.Status == OfferStatus.Active, ct);
        var freeLeft = Math.Max(0, rules.FreeSelections - page.FreeSelectionsUsed);
        return page.Balance - Math.Max(0, open - freeLeft) * rules.SelectionFee;
    }

    public void Post(Page page, LedgerType type, long amount, string note, Guid? orderId = null, Guid? refId = null)
    {
        page.Balance += amount;
        Db.LedgerEntries.Add(new LedgerEntry { PageId = page.Id, Type = type, Amount = amount, Note = note, OrderId = orderId, RefId = refId });
    }

    /// <summary>Charges the selection fee (or consumes a free selection) in the caller's transaction.</summary>
    public void CaptureSelection(Page page, Order order, BusinessRules rules)
    {
        var fee = TrustPolicy.SelectionFee(page.FreeSelectionsUsed, rules);
        var no = Shared.OrderNo(order.Number);
        if (fee == 0)
        {
            if (rules.BillingEnabled) page.FreeSelectionsUsed++;
            Post(page, LedgerType.FreeSelection, 0, texts.Get("ledger.freeSelection", no), order.Id);
        }
        else Post(page, LedgerType.SelectionFee, -fee, texts.Get("ledger.selectionFee", no), order.Id);
    }

    /// <summary>Gives back the fee (or the free credit) when the page is not at fault.</summary>
    public void ReleaseSelection(Page page, Order order)
    {
        var entry = Db.LedgerEntries.Local.FirstOrDefault(e => e.OrderId == order.Id)
                    ?? Db.LedgerEntries.FirstOrDefault(e => e.OrderId == order.Id && (e.Type == LedgerType.SelectionFee || e.Type == LedgerType.FreeSelection));
        if (entry is null) return;
        if (entry.Type == LedgerType.FreeSelection) { if (page.FreeSelectionsUsed > 0) page.FreeSelectionsUsed--; return; }
        Post(page, LedgerType.Adjustment, -entry.Amount, texts.Get("ledger.refund", Shared.OrderNo(order.Number)), order.Id);
    }

    public async Task<Result<WalletDto>> WalletAsync(CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        var rules = await s.Rules(ct);
        var ledger = await Db.LedgerEntries.Where(e => e.PageId == page.Id).OrderByDescending(e => e.CreatedAt).Take(30)
            .Select(e => new LedgerDto(e.Id, e.Type.ToString(), e.Amount, e.Note, e.CreatedAt)).ToListAsync(ct);
        var topups = await Db.TopUps.Where(t => t.PageId == page.Id).OrderByDescending(t => t.CreatedAt).Take(30).ToListAsync(ct);
        var available = await AvailableAsync(page, rules, ct);
        return new WalletDto(page.Balance, available == long.MaxValue ? page.Balance : available, page.FreeSelectionsUsed, rules.FreeSelections,
            rules.SelectionFee, rules.BillingEnabled, ledger.Select(l => l with { Type = Lower(l.Type) }).ToList(),
            topups.Select(ToDto).ToList(), rules.TopUpMethods, rules.MinTopUp);
    }

    public async Task<Result<CursorPage<LedgerDto>>> LedgerAsync(string? cursor, int take, CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        var q = Db.LedgerEntries.Where(e => e.PageId == page.Id);
        if (DateTime.TryParse(cursor, null, System.Globalization.DateTimeStyles.RoundtripKind, out var before)) q = q.Where(e => e.CreatedAt < before);
        var rows = await q.OrderByDescending(e => e.CreatedAt).Take(take).ToListAsync(ct);
        return new CursorPage<LedgerDto>(rows.Select(e => new LedgerDto(e.Id, e.Type.Code(), e.Amount, e.Note, e.CreatedAt)).ToList(),
            rows.Count == take ? rows[^1].CreatedAt.ToString("O") : null);
    }

    public async Task<Result<TopUpDto>> TopUpAsync(CreateTopUpDto dto, CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        var rules = await s.Rules(ct);
        if (!rules.TopUpMethods.Contains(dto.Method)) return Errors.BadEnum("method");
        if (dto.Amount < rules.MinTopUp) return Error.Validation("topup.min", "Amount below minimum");
        var reference = dto.TransactionRef.Trim();
        if (await Db.TopUps.AnyAsync(t => t.TransactionRef == reference, ct)) return Error.Conflict("topup.duplicate_ref", "Reference already used");
        var t = new TopUp { PageId = page.Id, Method = dto.Method, TransactionRef = reference, Amount = dto.Amount, ReceiptKey = dto.ReceiptKey, Status = TopUpStatus.Pending };
        Db.TopUps.Add(t);
        await Db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    public async Task<Result<PageMeDto>> SetEarlyAccessAsync(bool on, PageService pages, CancellationToken ct)
    {
        var res = await s.ActivePageAsync(ct);
        if (!res.IsSuccess) return res.Error!;
        var page = res.Value!;
        var rules = await s.Rules(ct);
        if (!on) page.EarlyAccessUntil = null;
        else if (!page.HasEarlyAccess(s.Now))
        {
            if (rules.BillingEnabled)
            {
                if (await AvailableAsync(page, rules, ct) < rules.EarlyAccessMonthly) return Error.Payment("wallet.insufficient", "Top up your balance");
                Post(page, LedgerType.EarlyAccess, -rules.EarlyAccessMonthly, texts.Get("ledger.earlyAccess"));
            }
            page.EarlyAccessUntil = s.Now.AddDays(rules.EarlyAccessDays);
        }
        await Db.SaveChangesAsync(ct);
        return await pages.MeAsync(ct);
    }

    private static string Lower(string s) => char.ToLowerInvariant(s[0]) + s[1..];
    public static TopUpDto ToDto(TopUp t) => new(t.Id, t.Method, t.TransactionRef, t.Amount, t.CreatedAt, t.Status.Code());
}

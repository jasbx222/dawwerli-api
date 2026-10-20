using Dawwerli.Application.Abstractions;
using Dawwerli.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Dawwerli.Infrastructure.Persistence;

/// <summary>Demo accounts and requests for the QA scenario. Idempotent. Login OTP is the fixed code.</summary>
public sealed class ScenarioSeeder(AppDbContext db, IStorage storage, IPhoneProtector phones, IClock clock, ILogger<ScenarioSeeder> log)
{
    private const string Marker = "nahr.store";

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (await db.Pages.AnyAsync(p => p.InstagramHandle == Marker, ct)) return;

        var now = clock.UtcNow;
        var gov = await db.Governorates.ToDictionaryAsync(g => g.Name, g => g.Id, ct);
        var cat = await db.Categories.ToDictionaryAsync(c => c.Slug, c => c.Id, ct);
        var baghdad = gov["بغداد"];
        var basra = gov["البصرة"];
        var neighbors = new[] { "الأنبار", "صلاح الدين", "ديالى", "واسط", "بابل" }.Select(n => gov[n]).ToArray();
        var shop = await PhotoAsync("shop_2.webp", ct);
        var shoeA = await PhotoAsync("cat_shoes_1.webp", ct);
        var shoeB = await PhotoAsync("cat_shoes_2.webp", ct);
        var shoeC = await PhotoAsync("shoe.jpg", ct);
        var shoeD = await PhotoAsync("cat_shoes_1.jpg", ct);
        var shoeE = await PhotoAsync("cat_shoes_2.jpg", ct);
        var shirt = await PhotoAsync("shirt.jpg", ct);
        var jacket = await PhotoAsync("jacket.jpg", ct);
        var phone = await PhotoAsync("phone.jpg", ct);

        var ahmed = Customer("07701110001", "أحمد", baghdad, "الكرادة");
        var sara = Customer("07701110002", "سارة", baghdad, "المنصور");
        var ali = Customer("07701110003", "علي", baghdad, "الأعظمية");
        var hasan = Customer("07701110004", "حسن", baghdad, "الكرادة");
        var noor = Customer("07701110005", "نور", baghdad, "زيونة");
        var layla = Customer("07701110006", "ليلى", baghdad, "الدورة");
        var mazen = Customer("07701110007", "مازن", baghdad, "الشعب");
        var kareem = Customer("07701110008", "كريم", baghdad, "البياع");
        var zaid = Customer("07701110009", "زيد", baghdad, "الحرية");
        var huda = Customer("07701110010", "هدى", baghdad, "الجادرية");
        var fahad = Customer("07701110011", "فهد", baghdad, "الكاظمية");
        kareem.PostingBannedUntil = now.AddDays(7);
        kareem.BanCount = 1;

        var nahrUser = ShopUser("07702220001", "متجر النهر", baghdad);
        var rafidainUser = ShopUser("07702220002", "أحذية الرافدين", baghdad);
        var styleUser = ShopUser("07702220003", "ستايل بغداد", baghdad);
        var pendingUser = ShopUser("07702220004", "صفحة قيد التوثيق", baghdad);
        var stoppedUser = ShopUser("07702220005", "صفحة موقوفة", baghdad);
        var basraUser = ShopUser("07702220006", "بصرة شوز", basra);
        var quietUser = ShopUser("07702220007", "صفحة هادئة", baghdad);
        var emptyUser = ShopUser("07702220008", "رصيد صفر", baghdad);
        var blockedUser = ShopUser("07702220009", "صفحة محظورة", baghdad);
        var offer4User = ShopUser("07702220010", "عرض أربعة", baghdad);
        var offer5User = ShopUser("07702220011", "عرض خمسة", baghdad);

        db.Users.AddRange(ahmed, sara, ali, hasan, noor, layla, mazen, kareem, zaid, huda, fahad,
            nahrUser, rafidainUser, styleUser, pendingUser, stoppedUser, basraUser, quietUser, emptyUser, blockedUser, offer4User, offer5User);
        db.Strikes.AddRange(
            new Strike { UserId = kareem.Id, Reason = "اختار وما كمل" },
            new Strike { UserId = kareem.Id, Reason = "رفض الاستلام بدون سبب" },
            new Strike { UserId = kareem.Id, Reason = "بلاغ «ما ردت» غير صحيح" });
        await db.SaveChangesAsync(ct);

        var nahr = Page(nahrUser, "متجر النهر", Marker, PageStatus.Verified, shop, 4.9, 80, 120, 2, 50000);
        var rafidain = Page(rafidainUser, "أحذية الرافدين", "rafidain.shoes", PageStatus.Verified, shop, 4.2, 18, 8, 1, 20000);
        var style = Page(styleUser, "ستايل بغداد", "style.bgd", PageStatus.Verified, shop, 3.6, 6, 2, 0, 10000);
        var pending = Page(pendingUser, "صفحة قيد التوثيق", "pending.shop", PageStatus.Pending, shop, 0, 0, 0, 0, 0);
        var stopped = Page(stoppedUser, "صفحة موقوفة", "stopped.shop", PageStatus.Suspended, shop, 3.1, 4, 1, 0, 0);
        stopped.SuspendedUntil = now.AddDays(7);
        var basraPage = Page(basraUser, "بصرة شوز", "basra.shoes", PageStatus.Verified, shop, 4.0, 5, 3, 0, 15000);
        var quiet = Page(quietUser, "صفحة هادئة", "quiet.shop", PageStatus.Verified, shop, 4.4, 9, 4, 0, 15000);
        quiet.WorkFrom = 3;
        quiet.WorkTo = 4;
        var empty = Page(emptyUser, "رصيد صفر", "empty.shop", PageStatus.Verified, shop, 4.0, 3, 10, 10, 0);
        var blocked = Page(blockedUser, "صفحة محظورة", "blocked.shop", PageStatus.Verified, shop, 3.8, 4, 2, 0, 8000);
        var offer4 = Page(offer4User, "عرض أربعة", "offer4.shop", PageStatus.Verified, shop, 4.1, 7, 5, 0, 12000);
        var offer5 = Page(offer5User, "عرض خمسة", "offer5.shop", PageStatus.Verified, shop, 4.5, 11, 6, 0, 12000);
        db.Pages.AddRange(nahr, rafidain, style, pending, stopped, basraPage, quiet, empty, blocked, offer4, offer5);
        await db.SaveChangesAsync(ct);

        Link(nahr, cat["shoes"], cat["clothes"], baghdad);
        Link(rafidain, cat["shoes"], null, baghdad);
        Link(style, cat["shoes"], null, baghdad);
        Link(pending, cat["shoes"], null, baghdad);
        Link(stopped, cat["shoes"], null, baghdad);
        Link(basraPage, cat["shoes"], null, basra);
        Link(quiet, cat["shoes"], null, baghdad);
        Link(empty, cat["shoes"], null, baghdad);
        Link(blocked, cat["shoes"], null, baghdad);
        Link(offer4, cat["shoes"], null, baghdad);
        Link(offer5, cat["shoes"], null, baghdad);
        db.Blocks.Add(new Block { BlockerUserId = layla.Id, BlockedUserId = blockedUser.Id });
        db.PageMarks.AddRange(
            new PageMark { PageId = style.Id, Kind = PageMarkKind.NoReply, Title = "ما ردّيت على الزبون" },
            new PageMark { PageId = style.Id, Kind = PageMarkKind.NoReply, Title = "ما ردّيت على الزبون" });
        await db.SaveChangesAsync(ct);

        var shoePages = new[] { nahr, rafidain, style, empty, blocked, offer4, offer5 };
        var ago = now.AddMinutes(-20);

        var saraShoes = await RequestAsync(sara, cat["shoes"], baghdad, "قندرة نايك قياس 42 سوداء", """{"size":"42","color":"أسود"}""", 70000, RequestStatus.Open, shoeA, ago, ct);
        var saraClothes = await RequestAsync(sara, cat["clothes"], baghdad, "تيشيرت قطن قياس L أبيض", """{"size":"L","color":"أبيض"}""", 25000, RequestStatus.Open, shirt, ago, ct);
        var saraPhones = await RequestAsync(sara, cat["phones"], baghdad, "آيفون 13 ذاكرة 128", """{"size":"128GB","color":"أسود"}""", 450000, RequestStatus.Open, phone, ago, ct);
        var aliReq = await RequestAsync(ali, cat["shoes"], baghdad, "حذاء رياضي قياس 43 أبيض", """{"size":"43","color":"أبيض"}""", 90000, RequestStatus.Open, shoeB, ago, ct);
        var laylaReq = await RequestAsync(layla, cat["shoes"], baghdad, "بوط شتوي قياس 40 أسود", """{"size":"40","color":"أسود"}""", 80000, RequestStatus.Open, shoeC, ago, ct);
        var locked = await RequestAsync(mazen, cat["shoes"], baghdad, "سنيكرز قياس 44 رمادي", """{"size":"44","color":"رمادي"}""", 100000, RequestStatus.Locked, shoeD, ago, ct);
        var expanded = await RequestAsync(huda, cat["shoes"], baghdad, "حذاء رسمي قياس 41 أسود", """{"size":"41","color":"أسود"}""", 75000, RequestStatus.Expanded, shoeE, now.AddMinutes(-20), ct);
        expanded.WidenedTo = neighbors;
        expanded.ExpandsAt = now.AddMinutes(-5);
        var expired = await RequestAsync(fahad, cat["shoes"], baghdad, "حذاء قديم ما انعرض", """{"size":"42","color":"أسود"}""", 40000, RequestStatus.Expired, shoeA, now.AddHours(-25), ct);
        expired.ExpiresAt = now.AddHours(-1);
        var cancelled = await RequestAsync(fahad, cat["clothes"], baghdad, "جاكيت ألغاه الزبون", """{"size":"M","color":"أسود"}""", 50000, RequestStatus.Cancelled, jacket, ago, ct);
        var hasanReq = await RequestAsync(hasan, cat["shoes"], baghdad, "نايك اير قياس 42 أسود", """{"size":"42","color":"أسود"}""", 95000, RequestStatus.Selected, shoeB, ago, ct);
        var noorReq = await RequestAsync(noor, cat["shoes"], baghdad, "حذاء وصل ويبي تقييم", """{"size":"39","color":"أبيض"}""", 60000, RequestStatus.Selected, shoeC, ago, ct);
        var zaidReq = await RequestAsync(zaid, cat["shoes"], baghdad, "حذاء الصفحة تواصلت وياه", """{"size":"42","color":"أزرق"}""", 85000, RequestStatus.Selected, shoeE, ago, ct);

        Dispatch(saraShoes, shoePages, ago);
        Dispatch(saraClothes, new[] { nahr }, ago);
        Dispatch(aliReq, shoePages, ago);
        Dispatch(laylaReq, shoePages.Where(p => p.Id != blocked.Id), ago);
        Dispatch(locked, shoePages, ago);
        Dispatch(expanded, shoePages, ago);
        Dispatch(hasanReq, new[] { nahr, rafidain }, ago);
        Dispatch(noorReq, new[] { rafidain }, ago);
        Dispatch(zaidReq, new[] { style }, ago);

        var aliCheap = Offer(aliReq, nahr, shoeC, 45000, 24, "نفس الصورة، توصيل للكرادة");
        var aliOther = Offer(aliReq, rafidain, shoeE, 52000, 48, "أصلي مع علبة");
        aliReq.OffersCount = 2;
        var lockOffers = new[]
        {
            Offer(locked, nahr, shoeA, 40000, 24, "أرخص عرض"),
            Offer(locked, rafidain, shoeB, 48000, 36, "عرض ثاني"),
            Offer(locked, style, shoeC, 55000, 24, "عرض ثالث"),
            Offer(locked, offer4, shoeE, 62000, 48, "عرض رابع"),
            Offer(locked, offer5, shoeD, 70000, 12, "عرض خامس"),
        };
        locked.OffersCount = 5;
        var hasanOffer = Offer(hasanReq, nahr, shoeA, 64000, 24, "جاهز للتوصيل");
        hasanOffer.Status = OfferStatus.Selected;
        hasanReq.OffersCount = 1;
        var noorOffer = Offer(noorReq, rafidain, shoeB, 38000, 24, "تم التسليم");
        noorOffer.Status = OfferStatus.Selected;
        noorReq.OffersCount = 1;
        var zaidOffer = Offer(zaidReq, style, shoeD, 50000, 24, "تواصلت ويا الزبون");
        zaidOffer.Status = OfferStatus.Selected;
        zaidReq.OffersCount = 1;
        db.Offers.AddRange(aliCheap, aliOther, hasanOffer, noorOffer, zaidOffer);
        db.Offers.AddRange(lockOffers);

        var hasanOrder = await OrderAsync(hasanReq, hasanOffer, nahr, hasan, OrderStatus.InPurchase, now.AddHours(-2), ct);
        var noorOrder = await OrderAsync(noorReq, noorOffer, rafidain, noor, OrderStatus.Delivered, now.AddDays(-1), ct);
        noorOrder.Answer = DeliveryAnswer.Received;
        noorOrder.AnsweredAt = now.AddHours(-2);
        noorOrder.DeliveryAskedAt = now.AddHours(-3);
        var zaidOrder = await OrderAsync(zaidReq, zaidOffer, style, zaid, OrderStatus.InPurchase, now.AddHours(-2), ct);
        zaidOrder.ContactedAt = now.AddHours(-1);

        db.Disputes.Add(new Dispute
        {
            OpenedByUserId = styleUser.Id, OrderId = zaidOrder.Id, SubjectId = zaid.Id, Type = DisputeType.NoReply,
            Text = "الصفحة تكول تواصلت والزبون ضغط «ما ردت»", Status = CaseStatus.Open, DecideBy = now.AddHours(48),
        });
        db.Reports.Add(new Report
        {
            ReporterUserId = noor.Id, Target = ReportTarget.Page, TargetId = rafidain.Id, OrderId = noorOrder.Id, OfferId = noorOffer.Id,
            Reason = ReportReason.Mismatch, Text = "اللون مو نفس الصورة", PhotoKey = shoeE, Status = CaseStatus.Open,
        });
        db.TopUps.Add(new TopUp
        {
            PageId = empty.Id, Method = "ZainCash", TransactionRef = "SEED-ZC-1001", Amount = 25000, ReceiptKey = shop, Status = TopUpStatus.Pending,
        });
        db.LedgerEntries.Add(new LedgerEntry
        {
            PageId = nahr.Id, Type = LedgerType.FreeSelection, Amount = 0, OrderId = hasanOrder.Id, Note = "اختيار مجاني — طلب حسن",
        });
        db.Notifications.Add(new Notification
        {
            UserId = nahrUser.Id, Title = "طلب جديد يناسبك", Body = "زبون يدوّر على: حذاء رياضي قياس 43 أبيض", Route = $"/seller/requests/{aliReq.Id}/offer",
        });
        var admin = await db.Users.FirstOrDefaultAsync(u => u.Role == UserRole.Admin, ct);
        if (admin is not null)
        {
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = admin.Id, Action = "page.verify", EntityName = "Page", EntityId = nahr.Id.ToString(), Data = """{"name":"متجر النهر"}""",
            });
            db.SupportMessages.Add(new SupportMessage
            {
                UserId = ahmed.Id, Topic = "حساب", Text = "رسالة تجريبية من أحمد", Status = CaseStatus.Open,
            });
        }

        await db.SaveChangesAsync(ct);
        log.LogInformation("Seeded QA scenario accounts");
    }

    private User Customer(string phone, string name, int gov, string area) => new()
    {
        Role = UserRole.Customer, PhoneHash = phones.Hash(phone), PhoneEncrypted = phones.Encrypt(phone),
        Name = name, GovernorateId = gov, Area = area,
    };

    private User ShopUser(string phone, string name, int gov) => new()
    {
        Role = UserRole.Page, PhoneHash = phones.Hash(phone), PhoneEncrypted = phones.Encrypt(phone),
        Name = name, GovernorateId = gov, Area = "المحل",
    };

    private static Page Page(User user, string name, string handle, PageStatus status, string photo, double rating, int ratingCount, int deals, int freeUsed, long balance) => new()
    {
        UserId = user.Id, Name = name, InstagramHandle = handle, Status = status, ImageKey = photo, BioCode = "SEED",
        Rating = rating, RatingCount = ratingCount, Deals = deals, FreeSelectionsUsed = freeUsed, Balance = balance, Receiving = true,
    };

    private void Link(Page page, int categoryId, int? extraCategory, int govId)
    {
        db.PageCategories.Add(new PageCategory { PageId = page.Id, CategoryId = categoryId });
        if (extraCategory is int extra) db.PageCategories.Add(new PageCategory { PageId = page.Id, CategoryId = extra });
        db.PageGovernorates.Add(new PageGovernorate { PageId = page.Id, GovernorateId = govId });
    }

    private async Task<Request> RequestAsync(User customer, int categoryId, int govId, string description, string fields, long maxPrice, RequestStatus status, string photo, DateTime created, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var request = new Request
        {
            CustomerId = customer.Id, Number = await db.NextNumberAsync(AppDbContext.RequestNumberSeq, ct), Description = description,
            Mode = InputMode.Photo, MediaKey = photo, CategoryId = categoryId, Fields = fields, Condition = ItemCondition.New,
            MaxPrice = maxPrice, GovernorateId = govId, Status = status, CreatedAt = created, EarlyUntil = now, PublicDispatched = true,
            ExpandsAt = now.AddMinutes(15), ExpiresAt = created.AddHours(24),
        };
        db.Requests.Add(request);
        return request;
    }

    private void Dispatch(Request request, IEnumerable<Page> pages, DateTime at)
    {
        foreach (var page in pages)
            db.RequestDispatches.Add(new RequestDispatch { RequestId = request.Id, PageId = page.Id, DispatchedAt = at });
    }

    private Offer Offer(Request request, Page page, string photo, long price, int hours, string note) => new()
    {
        RequestId = request.Id, PageId = page.Id, Price = price, DeliveryHours = hours, Condition = ItemCondition.New,
        Note = note, PhotoKey = photo, PhotoStamp = $"{page.Name} {clock.UtcNow:yyyy-MM-dd HH:mm}", Status = OfferStatus.Active,
    };

    private async Task<Order> OrderAsync(Request request, Offer offer, Page page, User customer, OrderStatus status, DateTime selectedAt, CancellationToken ct)
    {
        var order = new Order
        {
            Number = await db.NextNumberAsync(AppDbContext.OrderNumberSeq, ct), RequestId = request.Id, OfferId = offer.Id,
            PageId = page.Id, CustomerId = customer.Id, Status = status, SelectedAt = selectedAt,
        };
        db.Orders.Add(order);
        return order;
    }

    private readonly Dictionary<string, string> _photos = new();

    private async Task<string> PhotoAsync(string file, CancellationToken ct)
    {
        if (_photos.TryGetValue(file, out var cached)) return cached;
        var path = Path.Combine(AppContext.BaseDirectory, "Persistence", "Seed", "media", "scenario", file);
        await using var img = File.OpenRead(path);
        var key = await storage.SaveAsync(img, Path.GetExtension(file), "seed", ct);
        _photos[file] = key;
        return key;
    }
}

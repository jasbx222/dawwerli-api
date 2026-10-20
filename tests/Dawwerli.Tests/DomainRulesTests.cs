using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;

namespace Dawwerli.Tests;

public class ContactFilterTests
{
    [Theory]
    [InlineData("رقمي 07701234567")]
    [InlineData("رقمي ٠٧٧٠ ١٢٣ ٤٥٦٧")]
    [InlineData("0770-123-4567")]
    [InlineData("تابعني @myshop")]
    [InlineData("www.shop.com")]
    [InlineData("https://instagram.com/x")]
    [InlineData("shop.iq")]
    public void Rejects_contact_info(string text) => Assert.True(ContactFilter.HasContact(text));

    [Theory]
    [InlineData("نايك اير فورس قياس 42")]
    [InlineData("السعر 55000 مع التوصيل")]
    [InlineData("")]
    public void Allows_normal_text(string text) => Assert.False(ContactFilter.HasContact(text));

    [Fact]
    public void Finds_banned_words() => Assert.Equal("مسدس", ContactFilter.FindBanned("أريد مسدس", ["سلاح", "مسدس"]));
}

public class OfferPolicyTests
{
    private static readonly BusinessRules Rules = new() { MaxOffers = 5, MaxEarlySlots = 2 };
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static Request Req(int offers = 0, int early = 0, bool inWindow = false, RequestStatus status = RequestStatus.Open) => new()
    {
        MaxPrice = 60000, OffersCount = offers, EarlyOffersCount = early, Status = status,
        EarlyUntil = inWindow ? Now.AddMinutes(2) : Now.AddMinutes(-1),
    };

    [Fact] public void Accepts_valid_offer() => Assert.Null(OfferPolicy.Check(Req(), 50000, "أصلي", false, Now, Rules));
    [Fact] public void Rejects_over_max_price() => Assert.Equal("offer.over_max", OfferPolicy.Check(Req(), 70000, "", false, Now, Rules)!.Code);
    [Fact] public void Rejects_when_full() => Assert.Equal("offer.full", OfferPolicy.Check(Req(offers: 5), 50000, "", false, Now, Rules)!.Code);
    [Fact] public void Rejects_closed_request() => Assert.Equal("offer.request_closed", OfferPolicy.Check(Req(status: RequestStatus.Selected), 50000, "", false, Now, Rules)!.Code);
    [Fact] public void Rejects_contact_in_note() => Assert.Equal("offer.contact", OfferPolicy.Check(Req(), 50000, "كلمني 07701234567", false, Now, Rules)!.Code);
    [Fact] public void Non_subscribers_wait_for_early_window() => Assert.Equal("offer.early_window", OfferPolicy.Check(Req(inWindow: true), 50000, "", false, Now, Rules)!.Code);
    [Fact] public void Early_pages_take_at_most_MaxEarlySlots() => Assert.Equal("offer.early_full", OfferPolicy.Check(Req(offers: 2, early: 2, inWindow: true), 50000, "", true, Now, Rules)!.Code);
    [Fact] public void Early_page_can_take_a_free_early_slot() => Assert.Null(OfferPolicy.Check(Req(offers: 1, early: 1, inWindow: true), 50000, "", true, Now, Rules));
    [Fact] public void After_window_everyone_competes() => Assert.Null(OfferPolicy.Check(Req(offers: 2, early: 2), 50000, "", false, Now, Rules));
}

public class TrustPolicyTests
{
    private static readonly BusinessRules Rules = new() { StrikesForBan = 3, BansForPermanent = 2, NoReplyMarksForSuspension = 3, BillingEnabled = true, FreeSelections = 10, SelectionFee = 1000 };

    [Fact] public void Third_strike_bans() { Assert.False(TrustPolicy.ShouldBan(2, Rules)); Assert.True(TrustPolicy.ShouldBan(3, Rules)); }
    [Fact] public void Second_ban_is_permanent() { Assert.False(TrustPolicy.IsPermanent(0, Rules)); Assert.True(TrustPolicy.IsPermanent(1, Rules)); }
    [Fact] public void Third_no_reply_mark_suspends() => Assert.True(TrustPolicy.ShouldSuspendForMarks(3, Rules));
    [Fact] public void First_selections_are_free() { Assert.Equal(0, TrustPolicy.SelectionFee(9, Rules)); Assert.Equal(1000, TrustPolicy.SelectionFee(10, Rules)); }

    [Fact]
    public void Billing_off_means_free()
    {
        var off = new BusinessRules { BillingEnabled = false, FreeSelections = 0, SelectionFee = 1000 };
        Assert.Equal(0, TrustPolicy.SelectionFee(50, off));
    }
}

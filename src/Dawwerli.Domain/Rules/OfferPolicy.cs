using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;

namespace Dawwerli.Domain.Rules;

public static class OfferPolicy
{
    /// <summary>
    /// Validates a new offer against the request. Early-access pages may only take
    /// <see cref="BusinessRules.MaxEarlySlots"/> of the slots during the early window.
    /// </summary>
    public static Error? Check(Request r, long price, string note, bool earlyPage, DateTime now, BusinessRules rules)
    {
        if (!r.AcceptsOffers) return Error.Conflict("offer.request_closed", "Request no longer accepts offers");
        if (r.OffersCount >= rules.MaxOffers) return Error.Conflict("offer.full", "Request already has the maximum offers");
        if (price <= 0) return Error.Validation("offer.price", "Price must be positive");
        if (price > r.MaxPrice) return Error.Rule("offer.over_max", "Price is above the customer's max price");
        if (ContactFilter.HasContact(note)) return Error.Rule("offer.contact", "Offer text has contact info");
        if (now < r.EarlyUntil)
        {
            if (!earlyPage) return Error.Conflict("offer.early_window", "Request is still in the early-access window");
            if (r.EarlyOffersCount >= rules.MaxEarlySlots) return Error.Conflict("offer.early_full", "Early-access slots are taken");
        }
        return null;
    }

    public static bool IsEarlyOffer(Request r, DateTime now) => now < r.EarlyUntil;
}

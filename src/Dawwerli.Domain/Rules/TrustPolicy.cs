namespace Dawwerli.Domain.Rules;

public static class TrustPolicy
{
    /// <summary>True when recent strikes reach the posting-ban threshold.</summary>
    public static bool ShouldBan(int strikesInWindow, BusinessRules r) => strikesInWindow >= r.StrikesForBan;

    /// <summary>True when the next ban is permanent.</summary>
    public static bool IsPermanent(int previousBans, BusinessRules r) => previousBans + 1 >= r.BansForPermanent;

    public static bool ShouldSuspendForMarks(int marksInWindow, BusinessRules r) => marksInWindow >= r.NoReplyMarksForSuspension;

    /// <summary>Selection fee owed for a page's next selection (0 while free or billing is off).</summary>
    public static long SelectionFee(int freeUsed, BusinessRules r) =>
        !r.BillingEnabled || freeUsed < r.FreeSelections ? 0 : r.SelectionFee;
}

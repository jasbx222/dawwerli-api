using Dawwerli.Domain.Common;

namespace Dawwerli.Domain.Entities;

/// <summary>Append-only. Amount is signed IQD (credit &gt; 0).</summary>
public class LedgerEntry : Entity
{
    public Guid PageId { get; set; }
    public LedgerType Type { get; set; }
    public long Amount { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? RefId { get; set; }
    public string Note { get; set; } = "";
}

public class TopUp : Entity
{
    public Guid PageId { get; set; }
    public string Method { get; set; } = "";
    public string TransactionRef { get; set; } = "";
    public long Amount { get; set; }
    public string ReceiptKey { get; set; } = "";
    public TopUpStatus Status { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

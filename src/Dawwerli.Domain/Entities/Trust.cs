using Dawwerli.Domain.Common;

namespace Dawwerli.Domain.Entities;

public class Report : Entity
{
    public Guid ReporterUserId { get; set; }
    public ReportTarget Target { get; set; }
    public Guid TargetId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? OfferId { get; set; }
    public ReportReason Reason { get; set; }
    public string Text { get; set; } = "";
    public CaseStatus Status { get; set; }
}

public class Block : Entity
{
    public Guid BlockerUserId { get; set; }
    public Guid BlockedUserId { get; set; }
}

public class Strike : Entity
{
    public Guid UserId { get; set; }
    public string Reason { get; set; } = "";
    public Guid? OrderId { get; set; }
    public bool Frozen { get; set; }
}

public class Dispute : Entity
{
    public Guid OpenedByUserId { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? SubjectId { get; set; }
    public DisputeType Type { get; set; }
    public string Text { get; set; } = "";
    public string[] Photos { get; set; } = [];
    public CaseStatus Status { get; set; }
    public string? Decision { get; set; }
    public DateTime DecideBy { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

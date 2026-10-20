using Dawwerli.Domain.Common;

namespace Dawwerli.Domain.Entities;

public class Notification : Entity
{
    public Guid UserId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Route { get; set; } = "";
    public DateTime? ReadAt { get; set; }
}

public class IdempotencyRecord
{
    public string Key { get; set; } = "";
    public Guid UserId { get; set; }
    public int StatusCode { get; set; }
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class AuditLog : Entity
{
    public Guid ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityName { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Data { get; set; } = "{}";
}

/// <summary>Single row holding <see cref="Rules.BusinessRules"/> as JSON; editable by admin.</summary>
public class SettingsRecord
{
    public int Id { get; set; }
    public string Data { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; }
}

public class SupportMessage : Entity
{
    public Guid UserId { get; set; }
    public string Topic { get; set; } = "";
    public string Text { get; set; } = "";
    public CaseStatus Status { get; set; }
    public string? Reply { get; set; }
}

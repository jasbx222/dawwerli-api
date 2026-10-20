namespace Dawwerli.Domain.Entities;

public class Governorate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
}

public class GovernorateAdjacency
{
    public int GovernorateId { get; set; }
    public int NeighborId { get; set; }
}

public class Category
{
    public int Id { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Emoji { get; set; }
    public string? ImageKey { get; set; }
    public CategoryBadge Badge { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Words that make the suggester pick this category.</summary>
    public string[] Keywords { get; set; } = [];
    /// <summary>JSON schema: {"hint":"","required":["size"],"sizes":[],"colors":[],"priceSuggestions":[]}</summary>
    public string FieldSchema { get; set; } = "{}";
}

public class BannedKeyword
{
    public int Id { get; set; }
    public string Word { get; set; } = "";
}

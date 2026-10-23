using System.Text.Json;
using Dawwerli.Application.Abstractions;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Dawwerli.Infrastructure.Persistence.LiveSeed;
using Dawwerli.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dawwerli.Infrastructure.Persistence;

/// <summary>Applies migrations and inserts reference data (idempotent). The only place seed data lives.</summary>
public sealed class Seeder(AppDbContext db, IConfiguration config, IStorage storage, IPasswordHasher passwords,
    IOptions<AdminSeedOptions> admin, IOptions<LiveSeedOptions> live, LiveDataSeeder liveSeeder, ScenarioSeeder scenario, ILogger<Seeder> log)
{
    private sealed record CategorySeed(string Slug, string Name, string? Emoji, string Image, string Badge, string[] Keywords, JsonElement Schema);
    private sealed record CatalogSeed(string[] Governorates, Dictionary<string, string[]> Adjacency, CategorySeed[] Categories, string[] BannedKeywords);

    /// <param name="includeDemoData">False for the seed-live command: reference data only, no demo scenario or startup live seed.</param>
    public async Task RunAsync(CancellationToken ct = default, bool includeDemoData = true)
    {
        await db.Database.MigrateAsync(ct);
        var dir = Path.Combine(AppContext.BaseDirectory, "Persistence", "Seed");
        var seed = JsonSerializer.Deserialize<CatalogSeed>(await File.ReadAllTextAsync(Path.Combine(dir, "catalog.json"), ct),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        if (!await db.Settings.AnyAsync(x => x.Id == RulesProvider.SettingsId, ct))
        {
            var rules = config.GetSection("Business").Get<BusinessRules>() ?? throw new InvalidOperationException("Business section missing");
            db.Settings.Add(new SettingsRecord { Id = RulesProvider.SettingsId, Data = JsonSerializer.Serialize(rules), UpdatedAt = DateTime.UtcNow });
        }

        if (!await db.Governorates.AnyAsync(ct))
        {
            var govs = seed.Governorates.Select((n, i) => new Governorate { Id = i + 1, Name = n, SortOrder = i }).ToList();
            db.Governorates.AddRange(govs);
            var id = govs.ToDictionary(g => g.Name, g => g.Id);
            var pairs = seed.Adjacency.SelectMany(kv => kv.Value.SelectMany(n => new[] { (id[kv.Key], id[n]), (id[n], id[kv.Key]) })).Distinct();
            db.GovernorateAdjacency.AddRange(pairs.Select(p => new GovernorateAdjacency { GovernorateId = p.Item1, NeighborId = p.Item2 }));
        }

        if (!await db.Categories.AnyAsync(ct))
        {
            var order = 0;
            foreach (var c in seed.Categories)
            {
                await using var img = File.OpenRead(Path.Combine(dir, "media", c.Image));
                var key = await storage.SaveAsync(img, Path.GetExtension(c.Image), "categories", ct);
                Enum.TryParse<CategoryBadge>(c.Badge, true, out var badge);
                db.Categories.Add(new Category
                {
                    Slug = c.Slug, Name = c.Name, Emoji = c.Emoji, ImageKey = key, Badge = badge, SortOrder = order++,
                    Keywords = c.Keywords, FieldSchema = c.Schema.GetRawText(),
                });
            }
        }

        if (!await db.BannedKeywords.AnyAsync(ct))
            db.BannedKeywords.AddRange(seed.BannedKeywords.Select(w => new BannedKeyword { Word = w }));

        var a = admin.Value;
        if (!string.IsNullOrWhiteSpace(a.Email) && !string.IsNullOrWhiteSpace(a.Password) && !await db.Users.AnyAsync(u => u.Email == a.Email, ct))
        {
            db.Users.Add(new User { Role = UserRole.Admin, AdminRole = AdminRole.SuperAdmin, Email = a.Email, PasswordHash = passwords.Hash(a.Password), PhoneHash = $"admin:{a.Email}", Name = "Admin" });
            log.LogInformation("Seeded admin account");
        }
        await db.SaveChangesAsync(ct);
        if (!includeDemoData) return;
        // Seed:Live=true: one-time wipe + live data, guarded by a marker row (runs before the demo scenario, which then skips).
        if (live.Value.Live) await liveSeeder.RunOnStartupAsync(ct);
        await scenario.RunAsync(ct);
    }
}

namespace Dawwerli.Infrastructure.Persistence.LiveSeed;

/// <summary>
/// Product category of each live-seed template that does not belong to its page's own kit category: a phone shop's
/// earbuds and power banks are electronics, a home shop's fridge is an appliance, its sofa furniture, and so on.
/// Everything else keeps the kit's category. Keyed by template name, so a template shared by several kits (the 4K TV,
/// the round mirror) lands in one category everywhere. Lookups never touch the seed's random stream.
/// </summary>
internal static class LiveSeedCategories
{
    public static readonly IReadOnlyDictionary<string, string> ByTemplate = new Dictionary<string, string>
    {
        // shared wall templates
        ["شاشة سمارت 4K"] = "electronics",
        ["مراية دائرية بإطار ذهبي"] = "home",
        // phones kit
        ["ايربودز برو الجيل الثاني"] = "electronics",
        ["باور بانك 20000 ملي أمبير"] = "electronics",
        // home kit
        ["طقم قنفات 7 مقاعد"] = "furniture",
        ["ميز طعام 6 كراسي"] = "furniture",
        ["دولاب ملابس 4 أبواب"] = "furniture",
        ["سرير نفرين مع الدوشك"] = "furniture",
        ["غسالة أوتوماتيك 9 كيلو"] = "appliances",
        ["غسالة حوضين"] = "appliances",
        ["ثلاجة 14 قدم"] = "appliances",
        ["مجمدة عمودية"] = "appliances",
        ["سبلت 1.5 طن انفيرتر"] = "appliances",
        ["سبلت 2 طن"] = "appliances",
        ["طباخ غاز 5 عيون"] = "appliances",
        ["قلاية هوائية 6 لتر"] = "appliances",
        ["مكنسة كهربائية"] = "appliances",
        // electronics kit
        ["بلايستيشن 5 سلم"] = "gaming",
        ["يد بلايستيشن DualSense"] = "gaming",
        ["إكس بوكس سيريس S"] = "gaming",
        ["يد تحكم إكس بوكس"] = "gaming",
        // care kit
        ["أحمر شفاه مات"] = "beauty",
        ["ماسكارا مقاومة للماء"] = "beauty",
        ["طقم مكياج متكامل"] = "beauty",
        // gifts kit
        ["باقة ورد جوري أحمر"] = "flowers",
        ["باقة ورد تخرج"] = "flowers",
        // furniture kit
        ["لوحة كانفاس تجريدية"] = "home",
        ["إطار صور خشب منقوش"] = "home",
        ["سجادة جدارية فارسية"] = "home",
    };

    /// <summary>Category slug for a product generated from <paramref name="template"/> on a page of kit <paramref name="kitSlug"/>.</summary>
    public static string SlugOf(LiveSeedData.ProductSeed template, string kitSlug) =>
        ByTemplate.TryGetValue(template.Name, out var slug) ? slug : kitSlug;

    /// <summary>
    /// The template of a seeded product by name alone, across every kit (longest match wins), for rows whose category no
    /// longer tells the kit (e.g. after <see cref="ByTemplate"/> moved them).
    /// </summary>
    public static LiveSeedData.ProductSeed? AnyTemplateOf(string productName)
    {
        LiveSeedData.ProductSeed? best = null;
        foreach (var kit in LiveSeedData.Kits)
            if (LiveSeedDimensions.TemplateOf(kit, productName) is { } t && (best is null || t.Name.Length > best.Name.Length))
                best = t;
        return best;
    }
}

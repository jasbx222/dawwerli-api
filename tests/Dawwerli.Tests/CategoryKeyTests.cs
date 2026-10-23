using System.Net.Http.Json;
using System.Text.Json;

namespace Dawwerli.Tests;

/// <summary>Clients pick category icons by slug, so every category surface must carry it.</summary>
[Collection("api")]
public class CategoryKeyTests(ApiFactory factory)
{
    [Fact]
    public async Task Catalog_categories_carry_slug_and_keep_emoji_field()
    {
        var c = await factory.ClientAsync();
        var cats = await c.GetFromJsonAsync<JsonElement>("/api/v1/categories");
        Assert.True(cats.GetArrayLength() > 0);
        foreach (var cat in cats.EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(cat.GetProperty("slug").GetString()));
            Assert.True(cat.TryGetProperty("emoji", out _));
        }
    }

    [Fact]
    public async Task Home_categories_carry_the_same_slug_as_the_catalog()
    {
        var c = await factory.ClientAsync();
        var bySlug = (await c.GetFromJsonAsync<JsonElement>("/api/v1/categories")).EnumerateArray()
            .ToDictionary(x => x.GetProperty("id").GetInt32(), x => x.GetProperty("slug").GetString());
        var home = await c.GetFromJsonAsync<JsonElement>("/api/v1/home");
        var tiles = home.GetProperty("categories").EnumerateArray().ToList();
        Assert.NotEmpty(tiles);
        foreach (var t in tiles)
            Assert.Equal(bySlug[t.GetProperty("id").GetInt32()], t.GetProperty("slug").GetString());
    }
}

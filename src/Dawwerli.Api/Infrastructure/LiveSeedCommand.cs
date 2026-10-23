using Dawwerli.Infrastructure.Persistence;
using Dawwerli.Infrastructure.Persistence.LiveSeed;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dawwerli.Api.Infrastructure;

/// <summary>
/// <c>dotnet Dawwerli.Api.dll seed-live [--wipe [--include-real-users]] [--backfill-dimensions] [--confirm] [--dry-run] [--seed 42] [counts]</c>:
/// wipes seeded data and seeds live-looking data (or only adds sizes to the seeded products), then exits without starting the web host.
/// </summary>
public static class LiveSeedCommand
{
    public const string Name = "seed-live";

    private static readonly Dictionary<string, Action<LiveSeedOptions, int>> Counts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["--seed"] = (o, v) => o.RandomSeed = v,
        ["--pages"] = (o, v) => o.Pages = v,
        ["--product-pages"] = (o, v) => o.ProductPages = v,
        ["--products-min"] = (o, v) => o.MinProductsPerPage = v,
        ["--products-max"] = (o, v) => o.MaxProductsPerPage = v,
        ["--customers"] = (o, v) => o.Customers = v,
        ["--offers"] = (o, v) => o.Offers = v,
        ["--store-orders"] = (o, v) => o.StoreOrders = v,
        ["--ads"] = (o, v) => o.Ads = v,
        ["--featured-pages"] = (o, v) => o.FeaturedPages = v,
        ["--history-days"] = (o, v) => o.HistoryDays = v,
    };

    private const string Usage = """
        Usage: dotnet Dawwerli.Api.dll seed-live [--wipe [--include-real-users]] [--backfill-dimensions] [--confirm] [--dry-run] [--seed 42]
                 [--pages 500] [--product-pages 500] [--products-min 50] [--products-max 50] [--customers 50]
                 [--offers 50] [--store-orders 90] [--ads 50] [--featured-pages 8] [--history-days 180]
          --dry-run  read-only: print the target database, current counts and the plan
          --wipe     delete all non-admin data first (admins, settings, catalog and audit log are kept); refused when
                     real (non-seeded) users exist, unless --include-real-users is also given
          --include-real-users  with --wipe: delete real users and everything they own too
          --backfill-dimensions update seeded products in place; deletes nothing: sizes (cm) where missing,
                                wall kind + variant size/group for TVs, mirrors, art, frames and rugs
                                (also when they already had a size), and the missing wall products
          --confirm  required for anything that writes
        Defaults come from the "Seed" configuration section. Seller-portal logins and the app phone + password
        login for every page owner are created only when Seed__SellerPassword is set in the environment.
        """;

    /// <summary>Returns the process exit code: 0 done, 1 refused or failed, 2 bad arguments.</summary>
    public static async Task<int> RunAsync(IServiceProvider services, IConfiguration config, string[] args)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var o = sp.GetRequiredService<IOptions<LiveSeedOptions>>().Value.Copy();
        bool wipe = false, confirm = false, dryRun = false, includeReal = false, backfill = false;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--wipe": wipe = true; continue;
                case "--confirm": confirm = true; continue;
                case "--dry-run": dryRun = true; continue;
                case "--include-real-users": includeReal = true; continue;
                case "--backfill-dimensions": backfill = true; continue;
                case "-h" or "--help": Console.WriteLine(Usage); return 0;
            }
            if (Counts.TryGetValue(a, out var set) && i + 1 < args.Length && int.TryParse(args[i + 1], out var v)) { set(o, v); i++; continue; }
            Console.Error.WriteLine($"Unknown or incomplete argument: {a}");
            Console.Error.WriteLine(Usage);
            return 2;
        }
        if (backfill && wipe) { Console.Error.WriteLine("--backfill-dimensions and --wipe do not go together: pick one."); return 2; }
        if (includeReal && !wipe) { Console.Error.WriteLine("--include-real-users only applies to --wipe."); return 2; }
        if (o.Validate() is { } bad) { Console.Error.WriteLine($"Invalid counts: {bad}"); return 2; }

        var cs = new NpgsqlConnectionStringBuilder(config.GetConnectionString("Default") ?? "");
        Console.WriteLine($"Target database: host={cs.Host} port={cs.Port} db={cs.Database} user={cs.Username}");

        var seeder = sp.GetRequiredService<LiveDataSeeder>();
        if (dryRun)
        {
            Console.WriteLine($"Dry run (nothing is written). wipe={wipe} include_real_users={includeReal} backfill_dimensions={backfill}");
            Console.WriteLine(await seeder.DescribeAsync(o));
            return 0;
        }
        if (!confirm)
        {
            Console.Error.WriteLine("Refusing to write without --confirm. Check the target above, take a backup, then add --confirm (or use --dry-run).");
            return 1;
        }

        // Migrations + reference catalog + admin, without the demo scenario or the startup live seed.
        await sp.GetRequiredService<Seeder>().RunAsync(includeDemoData: false);
        if (backfill)
        {
            Console.WriteLine("Updating seeded products: sizes, wall kinds and wall products (nothing is deleted)...");
            var b = await seeder.BackfillDimensionsAsync();
            Console.WriteLine($"Done in {b.Elapsed.TotalSeconds:F1}s: {string.Join(", ", b.Created.Select(kv => $"{kv.Key}={kv.Value}"))}. Marker {LiveDataSeeder.MarkerName} written.");
            return 0;
        }
        Console.WriteLine(wipe ? "Wiping non-admin data and seeding..." : "Seeding (no wipe)...");
        var report = await seeder.RunAsync(o, wipe, onlyIfNoMarker: false, includeRealUsers: includeReal);
        if (report.Skipped)
        {
            Console.Error.WriteLine($"Nothing done: {report.Message}.");
            return 1;
        }
        foreach (var (table, rows) in report.Wiped) Console.WriteLine($"  wiped {table,-24} {rows,7}");
        foreach (var (table, rows) in report.Created) Console.WriteLine($"  created {table,-22} {rows,7}");
        Console.WriteLine($"Done in {report.Elapsed.TotalSeconds:F1}s. Marker {LiveDataSeeder.MarkerName} written.");
        if (report.Showcase is not null) Console.WriteLine($"Showcase page: {report.Showcase}");
        return 0;
    }
}

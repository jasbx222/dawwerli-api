using Dawwerli.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Dawwerli.Infrastructure.Persistence;

/// <summary>Used only by `dotnet ef`; connection from DAWWERLI_DESIGN_DB or a local default without secrets.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("DAWWERLI_DESIGN_DB") ?? "Host=localhost;Database=dawwerli_design";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(cs).UseSnakeCaseNamingConvention().Options;
        return new AppDbContext(options, new SystemClock());
    }
}

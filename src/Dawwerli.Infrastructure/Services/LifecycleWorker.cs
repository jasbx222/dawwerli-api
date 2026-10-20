using Dawwerli.Application.Services;
using Dawwerli.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dawwerli.Infrastructure.Services;

/// <summary>Runs <see cref="LifecycleService.TickAsync"/> on a fixed interval (stand-in for Hangfire recurring jobs).</summary>
public sealed class LifecycleWorker(IServiceScopeFactory scopes, IOptions<JobsOptions> options, ILogger<LifecycleWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.IntervalSeconds));
        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<LifecycleService>().TickAsync(ct);
                var cut = DateTime.UtcNow.AddHours(-options.Value.IdempotencyHours);
                await scope.ServiceProvider.GetRequiredService<AppDbContext>().IdempotencyRecords.Where(r => r.CreatedAt < cut).ExecuteDeleteAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Lifecycle tick failed");
            }
        }
    }
}

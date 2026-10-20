using Dawwerli.Application.Abstractions;
using Dawwerli.Infrastructure.Persistence;
using Dawwerli.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dawwerli.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, bool runWorker = true)
    {
        services.Configure<JwtOptions>(config.GetSection(JwtOptions.Section));
        services.Configure<SecurityOptions>(config.GetSection(SecurityOptions.Section));
        services.Configure<StorageOptions>(config.GetSection(StorageOptions.Section));
        services.Configure<SmsOptions>(config.GetSection(SmsOptions.Section));
        services.Configure<AdminSeedOptions>(config.GetSection(AdminSeedOptions.Section));
        services.Configure<JobsOptions>(config.GetSection(JobsOptions.Section));
        services.Configure<RegionOptions>(config.GetSection(RegionOptions.Section));
        services.Configure<OtpOptions>(config.GetSection(OtpOptions.Section));
        services.Configure<UserAuthOptions>(config.GetSection(UserAuthOptions.Section));

        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(config.GetConnectionString("Default")).UseSnakeCaseNamingConvention());
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddMemoryCache();

        var keys = config[$"{SecurityOptions.Section}:DataProtectionKeysPath"];
        var dp = services.AddDataProtection().SetApplicationName("dawwerli");
        if (!string.IsNullOrWhiteSpace(keys)) dp.PersistKeysToFileSystem(new DirectoryInfo(keys));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IPhoneProtector, PhoneProtector>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.Configure<AdminAuthOptions>(config.GetSection(AdminAuthOptions.Section));
        services.Configure<AuditPagingOptions>(config.GetSection(AuditPagingOptions.Section));
        services.AddSingleton<ITotpService, TotpService>();
        services.AddSingleton<LocalStorage>();
        services.AddSingleton<IStorage>(sp => sp.GetRequiredService<LocalStorage>());
        services.AddSingleton<ITextCatalog, ConfigTextCatalog>();
        services.AddSingleton<IPushSender, LogPushSender>();
        services.AddScoped<IRulesProvider, RulesProvider>();
        services.AddScoped<INotifier, Notifier>();
        services.AddScoped<ScenarioSeeder>();
        services.AddScoped<Seeder>();

        if (string.Equals(config[$"{SmsOptions.Section}:Provider"], "Http", StringComparison.OrdinalIgnoreCase))
            services.AddHttpClient<ISmsSender, HttpSmsSender>();
        else services.AddSingleton<ISmsSender, LogSmsSender>();

        if (runWorker) services.AddHostedService<LifecycleWorker>();
        return services;
    }
}

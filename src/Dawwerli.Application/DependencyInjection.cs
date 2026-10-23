using Dawwerli.Application.Services;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Dawwerli.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<Shared>();
        services.AddScoped<AuthService>();
        services.AddScoped<AccountService>();
        services.AddScoped<CatalogService>();
        services.AddScoped<DispatchService>();
        services.AddScoped<RequestService>();
        services.AddScoped<OfferService>();
        services.AddScoped<OrderService>();
        services.AddScoped<TrustService>();
        services.AddScoped<PageService>();
        services.AddScoped<WalletService>();
        services.AddScoped<AdsService>();
        services.AddScoped<HomeService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<AdminService>();
        services.AddScoped<LifecycleService>();
        services.AddScoped<StoreKit>();
        services.AddScoped<OfferOrderLink>();
        services.AddScoped<CourierService>();
        services.AddScoped<CourierTrackingService>();
        services.AddScoped<StoreOrderDesk>();
        services.AddScoped<PageStoreOrderService>();
        services.AddScoped<StoreService>();
        services.AddScoped<SellerPortalService>();
        services.AddScoped<AdminStoreService>();
        services.AddScoped<DiscoveryService>();
        services.AddScoped<FollowerNotifier>();
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        return services;
    }
}

using G4.Infrastructure.Authentication;
using G4.Infrastructure.Diagnostics;
using G4.Infrastructure.Disputes;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Integrations.PayPal;
using G4.Infrastructure.Integrations.Refunds;
using G4.Infrastructure.Integrations.Klarna;
using G4.Infrastructure.Integrations.Shipping;
using G4.Infrastructure.Integrations.VietMap;
using G4.Infrastructure.Notifications;
using G4.Infrastructure.Payments;
using G4.Infrastructure.Returns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace G4.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddDbContextFactory<ApplicationDbContext>(
            options => options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")),
            ServiceLifetime.Scoped
        );
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<NotificationProcessor>();
        services.AddScoped<IAdminOrderLookupService, AdminOrderLookupService>();
        services.AddScoped<IntegrationLogWriter>();
        services.AddSingleton<BufferedIntegrationLogWriter>();
        services.AddSingleton<IIntegrationLogWriter>(sp =>
            sp.GetRequiredService<BufferedIntegrationLogWriter>()
        );
        services.AddHostedService(sp => sp.GetRequiredService<BufferedIntegrationLogWriter>());
        services.AddScoped<CheckoutService>();
        services.AddScoped<ICheckoutService>(sp => sp.GetRequiredService<CheckoutService>());
        services.AddScoped<ImmediateCheckoutService>();
        services.AddScoped<IImmediateCardGateway, SimulatedImmediateCardGateway>();
        services.AddScoped<IBuyerCartService, BuyerCartService>();
        services.AddScoped<IBestOfferService, BestOfferService>();
        services.AddScoped<IBuyerAddressService, BuyerAddressService>();
        services.AddScoped<IAddressChangeService, AddressChangeService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<SellerIdentityHasher>();
        services.AddScoped<ISellerRegistrationService, SellerRegistrationService>();
        services.AddScoped<IBuyerRegistrationService, BuyerRegistrationService>();
        services.AddScoped<IShipmentService, ShipmentService>();
        services.AddScoped<IReturnService, ReturnService>();
        services.AddScoped<IPayPalPaymentService, PayPalPaymentService>();
        services.AddScoped<KlarnaPaymentService>();
        services.AddScoped<IPayPalCartPaymentService, PayPalCartPaymentService>();
        services.AddScoped<ISellerFinanceService, SellerFinanceService>();
        services.AddScoped<SellerFinanceReportService>();
        services.AddScoped<ISellerFeedbackService, SellerFeedbackService>();
        services.AddScoped<IProductFeedbackService, ProductFeedbackService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IDisputeService, DisputeService>();
        services.AddScoped<IRefundGateway, RefundGateway>();
        services.AddSingleton<CarrierAvailabilityState>();
        services.AddHttpClient<IPayPalGateway, PayPalSandboxGateway>();
        services.AddHttpClient<IKlarnaGateway, KlarnaGateway>();
        services.AddHttpClient<IPayPalBatchGateway, PayPalSandboxGateway>();
        services.AddHttpClient<ICarrierGateway, HttpCarrierGateway>();
        services.AddMemoryCache();
        // VietMap only accepts the key in the query string; the default HttpClient loggers
        // would write it (and the buyer's typed address) to the console log.
        services.AddHttpClient<IAddressLookupGateway, VietMapGateway>().RemoveAllLoggers();
        if (configuration.GetValue("Workers:Enabled", true))
            services.AddHostedService<CommerceMaintenanceWorker>();
        return services;
    }
}

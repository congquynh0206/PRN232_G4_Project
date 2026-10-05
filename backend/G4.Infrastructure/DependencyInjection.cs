using G4.Infrastructure.Integrations.PayPal;
using G4.Infrastructure.Integrations.Refunds;
using G4.Infrastructure.Integrations.Shipping;
using G4.Infrastructure.Finance;
using G4.Infrastructure.Disputes;
using G4.Infrastructure.Authentication;
using G4.Infrastructure.Notifications;
using G4.Infrastructure.Payments;
using G4.Infrastructure.Diagnostics;
using G4.Infrastructure.Returns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace G4.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")), ServiceLifetime.Scoped);
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<NotificationProcessor>();
        services.AddScoped<IntegrationLogWriter>();
        services.AddSingleton<BufferedIntegrationLogWriter>();
        services.AddSingleton<IIntegrationLogWriter>(sp=>sp.GetRequiredService<BufferedIntegrationLogWriter>());
        services.AddHostedService(sp=>sp.GetRequiredService<BufferedIntegrationLogWriter>());

        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IShipmentService, ShipmentService>();
        services.AddScoped<IReturnService, ReturnService>();
        services.AddScoped<IPayPalPaymentService, PayPalPaymentService>();
        services.AddScoped<ISellerFinanceService, SellerFinanceService>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IDisputeService, DisputeService>();
        services.AddScoped<IRefundGateway, RefundGateway>();
        services.AddSingleton<CarrierAvailabilityState>();
        services.AddHttpClient<IPayPalGateway, PayPalSandboxGateway>();
        services.AddHttpClient<ICarrierGateway, HttpCarrierGateway>();
        services.AddHostedService<CommerceMaintenanceWorker>();

        return services;
    }
}

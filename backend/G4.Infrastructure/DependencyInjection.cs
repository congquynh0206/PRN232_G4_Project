using G4.Infrastructure.Integrations.PayPal;
using G4.Infrastructure.Integrations.Refunds;
using G4.Infrastructure.Integrations.Shipping;
using G4.Infrastructure.Notifications;
using G4.Infrastructure.Payments;
using G4.Infrastructure.Returns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace G4.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            if (environment.IsDevelopment() && configuration.GetValue<bool>("Storage:UseInMemory"))
                options.UseInMemoryDatabase("G4Commerce");
            else
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));
        });

        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IShipmentService, ShipmentService>();
        services.AddScoped<IReturnService, ReturnService>();
        services.AddScoped<IPayPalPaymentService, PayPalPaymentService>();
        services.AddScoped<IRefundGateway, RefundGateway>();
        services.AddSingleton<CarrierAvailabilityState>();
        services.AddHttpClient<IPayPalGateway, PayPalSandboxGateway>();
        services.AddHttpClient<ICarrierGateway, HttpCarrierGateway>();
        services.AddHostedService<CommerceMaintenanceWorker>();

        return services;
    }
}

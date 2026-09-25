using Microsoft.EntityFrameworkCore;
using backend.Models;
using backend.Checkout;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Demo:UseInMemory"))
        options.UseInMemoryDatabase("G4CheckoutDemo");
    else
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});
builder.Services.AddScoped<MvpService>();
builder.Services.AddScoped<ShipmentService>();
builder.Services.AddScoped<ReturnService>();
builder.Services.AddScoped<PayPalPaymentService>();
builder.Services.AddScoped<IRefundGateway, RefundGateway>();
builder.Services.AddSingleton<DemoCarrierState>();
builder.Services.AddHttpClient<IPayPalGateway, PayPalSandboxGateway>();
builder.Services.AddHttpClient<ICarrierGateway, HttpCarrierGateway>();
builder.Services.AddHostedService<DemoMaintenanceWorker>();

var app = builder.Build();

app.UseExceptionHandler(error => error.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    context.Response.StatusCode = exception switch
    {
        ArgumentException => 400,
        KeyNotFoundException => 404,
        InvalidOperationException or DbUpdateException => 409,
        _ => 500
    };
    await context.Response.WriteAsJsonAsync(new
    {
        error = context.Response.StatusCode == 500 ? "An unexpected server error occurred" : exception?.Message
    });
}));

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

using System.Text;
using G4.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddSignalR();
var frontendOrigins =
    builder.Configuration.GetSection("Realtime:AllowedOrigins").Get<string[]>()
    ?? new[] { builder.Configuration["Frontend:PublicBaseUrl"] ?? "http://localhost:5131" };
builder.Services.AddCors(options =>
    options.AddPolicy(
        "CommerceRealtime",
        policy =>
            policy
                .WithOrigins(frontendOrigins)
                .AllowAnyHeader()
                .WithMethods("GET", "POST")
                .AllowCredentials()
    )
);
builder.Services.AddHostedService<G4.Api.Realtime.CommerceChangeWorker>();
var jwtKey =
    builder.Configuration["Authentication:JwtKey"]
    ?? throw new InvalidOperationException("Authentication:JwtKey must be configured");
builder
    .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Authentication:Issuer"],
            ValidAudience = builder.Configuration["Authentication:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (
                    context.Request.Path.StartsWithSegments("/hubs/commerce")
                    && context.Request.Query.TryGetValue("access_token", out var token)
                )
                    context.Token = token;
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()
);

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddInfrastructure(builder.Configuration);
G4.Infrastructure.Authentication.SellerIdentityHasher.ConfigureLocalKey(
    builder.Configuration,
    builder.Environment
);
var app = builder.Build();
app.UseExceptionHandler(error =>
    error.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        context.Response.StatusCode = exception switch
        {
            ArgumentException => 400,
            UnauthorizedAccessException => 403,
            KeyNotFoundException => 404,
            InvalidOperationException or DbUpdateException => 409,
            _ => 500,
        };
        await context.Response.WriteAsJsonAsync(
            new
            {
                error = context.Response.StatusCode == 500
                    ? "An unexpected server error occurred"
                    : exception?.Message,
            }
        );
    })
);

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.Use(
    async (context, next) =>
    {
        using var correlation = G4.Infrastructure.Diagnostics.IntegrationContext.Begin(
            null,
            Guid.NewGuid().ToString("N")
        );
        await next(context);
    }
);
app.UseAuthorization();
app.MapControllers();
app.MapHub<G4.Api.Realtime.CommerceHub>(
        "/hubs/commerce",
        options => options.CloseOnAuthenticationExpiration = true
    )
    .RequireCors("CommerceRealtime");
app.Run();

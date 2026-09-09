using System.Text;
using BillingPlatform.Identity.Application;
using BillingPlatform.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace BillingPlatform.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("BillingPlatform")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:BillingPlatform is required.");
        var jwtOptions = ReadAndValidateJwtOptions(configuration);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));

        services.AddDbContext<IdentityDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    postgres => postgres.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        "identity"))
                .UseSnakeCaseNamingConvention());

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<IdentityDbContext>()
            .AddDefaultTokenProviders();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidAudience = jwtOptions.Audience,
                    IssuerSigningKey = signingKey,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "email"
                };
            });

        services.AddAuthorization();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(jwtOptions);
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddScoped<IIdentityService, IdentityService>();

        return services;
    }

    private static JwtOptions ReadAndValidateJwtOptions(IConfiguration configuration)
    {
        var section = configuration.GetRequiredSection(JwtOptions.SectionName);
        var issuer = section[nameof(JwtOptions.Issuer)];
        var audience = section[nameof(JwtOptions.Audience)];
        var signingKey = section[nameof(JwtOptions.SigningKey)];
        var lifetimeMinutes = section.GetValue(nameof(JwtOptions.LifetimeMinutes), 60);

        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience are required.");
        }

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is required. Supply it through a secret or environment variable.");
        }

        if (Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is too weak. It must contain at least 32 UTF-8 bytes (256 bits).");
        }

        if (lifetimeMinutes is < 1 or > 1440)
        {
            throw new InvalidOperationException("Jwt:LifetimeMinutes must be between 1 and 1440.");
        }

        return new JwtOptions
        {
            Issuer = issuer,
            Audience = audience,
            SigningKey = signingKey,
            LifetimeMinutes = lifetimeMinutes
        };
    }
}

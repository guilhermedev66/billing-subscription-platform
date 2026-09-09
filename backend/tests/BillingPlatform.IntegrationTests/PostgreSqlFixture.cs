using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace BillingPlatform.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgreSQL integration";
}

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer database = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("billing_platform_tests")
        .WithUsername("billing")
        .WithPassword($"test-{Guid.NewGuid():N}")
        .Build();

    public Task InitializeAsync() => database.StartAsync();

    public Task DisposeAsync() => database.DisposeAsync().AsTask();

    public BillingPlatformApiFactory CreateFactory(
        Action<IServiceCollection>? configureTestServices = null) =>
        new(database.GetConnectionString(), configureTestServices);
}

public sealed class BillingPlatformApiFactory(
    string connectionString,
    Action<IServiceCollection>? configureTestServices)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:BillingPlatform", connectionString);
        builder.UseSetting(
            "Jwt:SigningKey",
            "integration-test-only-signing-key-with-at-least-32-bytes");

        if (configureTestServices is not null)
        {
            builder.ConfigureTestServices(configureTestServices);
        }
    }
}

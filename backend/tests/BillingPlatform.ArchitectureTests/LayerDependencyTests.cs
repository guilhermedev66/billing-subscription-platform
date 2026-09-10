using System.Reflection;
using NetArchTest.Rules;

namespace BillingPlatform.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private static readonly Assembly[] DomainAssemblies =
    [
        typeof(Identity.Domain.AssemblyMarker).Assembly,
        typeof(Organizations.Domain.AssemblyMarker).Assembly,
        typeof(Customers.Domain.AssemblyMarker).Assembly,
        typeof(Catalog.Domain.AssemblyMarker).Assembly,
        typeof(Subscriptions.Domain.AssemblyMarker).Assembly,
        typeof(Billing.Domain.AssemblyMarker).Assembly,
        typeof(Payments.Domain.AssemblyMarker).Assembly
    ];

    private static readonly Assembly[] ApplicationAssemblies =
    [
        typeof(Identity.Application.AssemblyMarker).Assembly,
        typeof(Organizations.Application.AssemblyMarker).Assembly,
        typeof(SimulationClock.Application.AssemblyMarker).Assembly,
        typeof(Customers.Application.AssemblyMarker).Assembly,
        typeof(Catalog.Application.AssemblyMarker).Assembly,
        typeof(Subscriptions.Application.AssemblyMarker).Assembly,
        typeof(Billing.Application.AssemblyMarker).Assembly,
        typeof(Payments.Application.AssemblyMarker).Assembly
    ];

    private static readonly Assembly[] InfrastructureAssemblies =
    [
        typeof(Identity.Infrastructure.AssemblyMarker).Assembly,
        typeof(Organizations.Infrastructure.AssemblyMarker).Assembly,
        typeof(SimulationClock.Infrastructure.AssemblyMarker).Assembly,
        typeof(Customers.Infrastructure.AssemblyMarker).Assembly,
        typeof(Catalog.Infrastructure.AssemblyMarker).Assembly,
        typeof(Subscriptions.Infrastructure.AssemblyMarker).Assembly,
        typeof(Billing.Infrastructure.AssemblyMarker).Assembly,
        typeof(Payments.Infrastructure.AssemblyMarker).Assembly
    ];

    [Fact]
    public void Domain_must_not_depend_on_outer_layers()
    {
        var result = Types.InAssemblies(DomainAssemblies)
            .ShouldNot()
            .HaveDependencyOnAny(".Application", ".Infrastructure", ".Api")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailures(result));
    }

    [Fact]
    public void Application_must_not_depend_on_infrastructure_or_api()
    {
        var result = Types.InAssemblies(ApplicationAssemblies)
            .ShouldNot()
            .HaveDependencyOnAny(".Infrastructure", ".Api")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailures(result));
    }

    [Fact]
    public void Infrastructure_must_not_depend_on_api()
    {
        var result = Types.InAssemblies(InfrastructureAssemblies)
            .ShouldNot()
            .HaveDependencyOn(".Api")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailures(result));
    }

    private static string FormatFailures(TestResult result) =>
        result.IsSuccessful
            ? string.Empty
            : $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}";
}

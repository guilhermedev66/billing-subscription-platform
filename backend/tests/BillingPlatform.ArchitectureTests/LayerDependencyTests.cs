using System.Reflection;
using NetArchTest.Rules;

namespace BillingPlatform.ArchitectureTests;

public sealed class LayerDependencyTests
{
    private static readonly Assembly[] DomainAssemblies =
    [
        typeof(Identity.Domain.AssemblyMarker).Assembly,
        typeof(Organizations.Domain.AssemblyMarker).Assembly
    ];

    private static readonly Assembly[] ApplicationAssemblies =
    [
        typeof(Identity.Application.AssemblyMarker).Assembly,
        typeof(Organizations.Application.AssemblyMarker).Assembly,
        typeof(SimulationClock.Application.AssemblyMarker).Assembly
    ];

    private static readonly Assembly[] InfrastructureAssemblies =
    [
        typeof(Identity.Infrastructure.AssemblyMarker).Assembly,
        typeof(Organizations.Infrastructure.AssemblyMarker).Assembly,
        typeof(SimulationClock.Infrastructure.AssemblyMarker).Assembly
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

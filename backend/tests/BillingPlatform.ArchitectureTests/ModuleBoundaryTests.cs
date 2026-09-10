using System.Reflection;
using NetArchTest.Rules;

namespace BillingPlatform.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
    public static TheoryData<string, Assembly[], string[]> Modules => new()
    {
        {
            "Identity",
            [
                typeof(Identity.Domain.AssemblyMarker).Assembly,
                typeof(Identity.Application.AssemblyMarker).Assembly,
                typeof(Identity.Infrastructure.AssemblyMarker).Assembly,
                typeof(Identity.Api.AssemblyMarker).Assembly
            ],
            [
                "BillingPlatform.Organizations.Domain",
                "BillingPlatform.Organizations.Infrastructure",
                "BillingPlatform.Organizations.Api",
                "BillingPlatform.SimulationClock.Infrastructure"
            ]
        },
        {
            "Organizations",
            [
                typeof(Organizations.Domain.AssemblyMarker).Assembly,
                typeof(Organizations.Application.AssemblyMarker).Assembly,
                typeof(Organizations.Infrastructure.AssemblyMarker).Assembly,
                typeof(Organizations.Api.AssemblyMarker).Assembly
            ],
            [
                "BillingPlatform.Identity.Domain",
                "BillingPlatform.Identity.Infrastructure",
                "BillingPlatform.Identity.Api",
                "BillingPlatform.SimulationClock.Infrastructure"
            ]
        },
        {
            "SimulationClock",
            [
                typeof(SimulationClock.Application.AssemblyMarker).Assembly,
                typeof(SimulationClock.Infrastructure.AssemblyMarker).Assembly
            ],
            [
                "BillingPlatform.Identity.Domain",
                "BillingPlatform.Identity.Infrastructure",
                "BillingPlatform.Identity.Api",
                "BillingPlatform.Organizations.Domain",
                "BillingPlatform.Organizations.Infrastructure",
                "BillingPlatform.Organizations.Api"
            ]
        },
        {
            "Customers",
            [
                typeof(Customers.Domain.AssemblyMarker).Assembly,
                typeof(Customers.Application.AssemblyMarker).Assembly,
                typeof(Customers.Infrastructure.AssemblyMarker).Assembly,
                typeof(Customers.Api.AssemblyMarker).Assembly
            ],
            [
                "BillingPlatform.Identity.Domain",
                "BillingPlatform.Identity.Infrastructure",
                "BillingPlatform.Identity.Api",
                "BillingPlatform.Organizations.Domain",
                "BillingPlatform.Organizations.Infrastructure",
                "BillingPlatform.Organizations.Api",
                "BillingPlatform.Catalog.Domain",
                "BillingPlatform.Catalog.Application",
                "BillingPlatform.Catalog.Infrastructure",
                "BillingPlatform.Catalog.Api",
                "BillingPlatform.SimulationClock.Infrastructure"
            ]
        },
        {
            "Catalog",
            [
                typeof(Catalog.Domain.AssemblyMarker).Assembly,
                typeof(Catalog.Application.AssemblyMarker).Assembly,
                typeof(Catalog.Infrastructure.AssemblyMarker).Assembly,
                typeof(Catalog.Api.AssemblyMarker).Assembly
            ],
            [
                "BillingPlatform.Identity.Domain",
                "BillingPlatform.Identity.Infrastructure",
                "BillingPlatform.Identity.Api",
                "BillingPlatform.Organizations.Domain",
                "BillingPlatform.Organizations.Infrastructure",
                "BillingPlatform.Organizations.Api",
                "BillingPlatform.Customers.Domain",
                "BillingPlatform.Customers.Application",
                "BillingPlatform.Customers.Infrastructure",
                "BillingPlatform.Customers.Api",
                "BillingPlatform.SimulationClock.Infrastructure"
            ]
        },
        {
            "Subscriptions",
            [
                typeof(Subscriptions.Domain.AssemblyMarker).Assembly,
                typeof(Subscriptions.Application.AssemblyMarker).Assembly,
                typeof(Subscriptions.Infrastructure.AssemblyMarker).Assembly,
                typeof(Subscriptions.Api.AssemblyMarker).Assembly
            ],
            [
                "BillingPlatform.Identity.Domain",
                "BillingPlatform.Identity.Infrastructure",
                "BillingPlatform.Identity.Api",
                "BillingPlatform.Organizations.Domain",
                "BillingPlatform.Organizations.Infrastructure",
                "BillingPlatform.Organizations.Api",
                "BillingPlatform.Customers.Domain",
                "BillingPlatform.Customers.Infrastructure",
                "BillingPlatform.Customers.Api",
                "BillingPlatform.Catalog.Domain",
                "BillingPlatform.Catalog.Infrastructure",
                "BillingPlatform.Catalog.Api",
                "BillingPlatform.SimulationClock.Infrastructure"
            ]
        }
    };

    [Theory]
    [MemberData(nameof(Modules))]
    public void Modules_must_not_reference_another_modules_implementation(
        string module,
        Assembly[] assemblies,
        string[] forbiddenNamespaces)
    {
        var result = Types.InAssemblies(assemblies)
            .ShouldNot()
            .HaveDependencyOnAny(forbiddenNamespaces)
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            $"{module} leaks across a module boundary. Failing types: " +
            string.Join(", ", result.FailingTypeNames ?? []));
    }
}

using BillingPlatform.SimulationClock.Application;
using Microsoft.Extensions.DependencyInjection;

namespace BillingPlatform.SimulationClock.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddSimulationClock(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IVirtualClock, VirtualClock>();

        return services;
    }
}

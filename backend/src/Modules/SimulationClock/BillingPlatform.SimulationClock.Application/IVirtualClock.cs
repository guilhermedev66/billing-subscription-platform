namespace BillingPlatform.SimulationClock.Application;

public interface IVirtualClock
{
    DateTimeOffset Now { get; }

    void FastForward(TimeSpan duration);

    void SetTime(DateTimeOffset newTime);

    void Reset();
}

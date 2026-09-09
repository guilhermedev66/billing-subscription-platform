using BillingPlatform.SimulationClock.Application;

namespace BillingPlatform.SimulationClock.Infrastructure;

public sealed class VirtualClock(TimeProvider timeProvider) : IVirtualClock
{
    private readonly Lock _lock = new();
    private TimeSpan _offset;

    public DateTimeOffset Now
    {
        get
        {
            lock (_lock)
            {
                return timeProvider.GetUtcNow().Add(_offset);
            }
        }
    }

    public void FastForward(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);

        lock (_lock)
        {
            _offset += duration;
        }
    }

    public void SetTime(DateTimeOffset newTime)
    {
        lock (_lock)
        {
            _offset = newTime.ToUniversalTime() - timeProvider.GetUtcNow();
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _offset = TimeSpan.Zero;
        }
    }
}

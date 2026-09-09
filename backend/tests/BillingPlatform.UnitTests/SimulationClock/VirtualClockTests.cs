using BillingPlatform.SimulationClock.Infrastructure;

namespace BillingPlatform.UnitTests.SimulationClock;

public sealed class VirtualClockTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FastForward_advances_from_current_virtual_time()
    {
        var timeProvider = new ManualTimeProvider(InitialTime);
        var clock = new VirtualClock(timeProvider);

        clock.FastForward(TimeSpan.FromDays(30));
        timeProvider.Advance(TimeSpan.FromHours(2));

        Assert.Equal(InitialTime.AddDays(30).AddHours(2), clock.Now);
    }

    [Fact]
    public void SetTime_can_move_the_clock_to_an_exact_instant()
    {
        var timeProvider = new ManualTimeProvider(InitialTime);
        var clock = new VirtualClock(timeProvider);
        var target = InitialTime.AddYears(1);

        clock.SetTime(target);

        Assert.Equal(target, clock.Now);
    }

    [Fact]
    public void Reset_returns_to_system_time()
    {
        var timeProvider = new ManualTimeProvider(InitialTime);
        var clock = new VirtualClock(timeProvider);
        clock.FastForward(TimeSpan.FromDays(1));

        clock.Reset();

        Assert.Equal(InitialTime, clock.Now);
    }

    [Fact]
    public void FastForward_rejects_non_positive_durations()
    {
        var clock = new VirtualClock(new ManualTimeProvider(InitialTime));

        Assert.Throws<ArgumentOutOfRangeException>(() => clock.FastForward(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.FastForward(TimeSpan.FromTicks(-1)));
    }

    private sealed class ManualTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialTime;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}

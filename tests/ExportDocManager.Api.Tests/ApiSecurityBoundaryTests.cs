using ExportDocManager.Api.Hosting;

namespace ExportDocManager.Api.Tests;

public sealed class ApiSecurityBoundaryTests
{
    [Fact]
    public void LoginAttempts_ShouldLockAfterFiveFailuresAndReleaseAfterLockWindow()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-07-24T00:00:00Z"));
        var attempts = new ApiLoginAttemptService(clock);

        for (int index = 0; index < ApiLoginAttemptService.MaximumFailures - 1; index++)
        {
            Assert.True(attempts.RecordFailure("alice", "10.0.0.5").Allowed);
        }

        var locked = attempts.RecordFailure("alice", "10.0.0.5");
        Assert.False(locked.Allowed);
        Assert.False(attempts.Evaluate("alice", "10.0.0.5").Allowed);

        clock.Advance(TimeSpan.FromMinutes(2).Add(TimeSpan.FromSeconds(1)));
        Assert.True(attempts.Evaluate("alice", "10.0.0.5").Allowed);
    }

    [Fact]
    public void LoginAttempts_ShouldApplyAccountAndIpDimensions()
    {
        var attempts = new ApiLoginAttemptService(new MutableTimeProvider(DateTimeOffset.UtcNow));

        for (int index = 0; index < ApiLoginAttemptService.MaximumFailures; index++)
        {
            attempts.RecordFailure("same-account", $"10.0.0.{index + 1}");
        }

        Assert.False(attempts.Evaluate("same-account", "10.0.0.99").Allowed);

        var otherAttempts = new ApiLoginAttemptService(new MutableTimeProvider(DateTimeOffset.UtcNow));
        for (int index = 0; index < ApiLoginAttemptService.MaximumFailures; index++)
        {
            otherAttempts.RecordFailure($"user-{index}", "10.0.0.42");
        }

        Assert.False(otherAttempts.Evaluate("another-user", "10.0.0.42").Allowed);
    }

    [Fact]
    public void LoginAttempts_ShouldClearAccountAndIpStateOnSuccess()
    {
        var attempts = new ApiLoginAttemptService();
        for (int index = 0; index < ApiLoginAttemptService.MaximumFailures - 1; index++)
        {
            attempts.RecordFailure("alice", "10.0.0.5");
        }

        attempts.RecordSuccess("alice", "10.0.0.5");
        Assert.True(attempts.Evaluate("alice", "10.0.0.5").Allowed);
        Assert.True(attempts.RecordFailure("alice", "10.0.0.5").Allowed);
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        public MutableTimeProvider(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; private set; }

        public override DateTimeOffset GetUtcNow() => UtcNow;

        public void Advance(TimeSpan amount) => UtcNow = UtcNow.Add(amount);
    }
}

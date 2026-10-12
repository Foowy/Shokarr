using Shokarr.BackgroundServices;
using Xunit;

namespace Shokarr.Tests;

public class ScanSchedulerServiceTests
{
    private static readonly DateTime s_now = new(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NextDelay_Overdue_RechecksInAMinute() =>
        Assert.Equal(TimeSpan.FromMinutes(1), ScanSchedulerService.NextDelay(s_now.AddHours(-3), s_now));

    [Fact]
    public void NextDelay_SoonDue_SleepsUntilDue() =>
        Assert.Equal(TimeSpan.FromMinutes(20), ScanSchedulerService.NextDelay(s_now.AddMinutes(20), s_now));

    [Fact]
    public void NextDelay_FarOff_IsCappedSoIntervalChangesApply() =>
        Assert.Equal(TimeSpan.FromHours(1), ScanSchedulerService.NextDelay(s_now.AddDays(30), s_now));
}

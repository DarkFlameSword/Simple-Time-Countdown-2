using TimeCountdown.Models;
using Xunit;

namespace TimeCountdown.Tests;

public sealed class CountdownThresholdsTests
{
    [Theory]
    [InlineData(-5, -3, 1, 2)]
    [InlineData(0, 0, 1, 2)]
    [InlineData(3, 2, 3, 4)]
    [InlineData(99, 400, 30, 180)]
    [InlineData(2, 8, 2, 8)]
    public void Normalize_KeepsBothLimitsInRangeAndUrgentAfterPerilous(int perilous, int urgent, int expectedPerilous, int expectedUrgent)
    {
        var thresholds = CountdownThresholds.Normalize(perilous, urgent);

        Assert.Equal(expectedPerilous, thresholds.PerilousDays);
        Assert.Equal(expectedUrgent, thresholds.UrgentDays);
    }

    [Theory]
    [InlineData(-0.001, CountdownStatus.Overdue)]
    [InlineData(0, CountdownStatus.Perilous)]
    [InlineData(0.99, CountdownStatus.Perilous)]
    [InlineData(1, CountdownStatus.Urgent)]
    [InlineData(7.99, CountdownStatus.Urgent)]
    [InlineData(8, CountdownStatus.Standing)]
    [InlineData(400, CountdownStatus.Standing)]
    public void Classify_UsesHalfOpenDayBands(double remainingDays, CountdownStatus expected)
    {
        var thresholds = new CountdownThresholds(PerilousDays: 1, UrgentDays: 8);

        Assert.Equal(expected, thresholds.Classify(TimeSpan.FromDays(remainingDays)));
    }
}

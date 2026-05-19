using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.Tests;

public sealed class CutoffPeriodTests
{
    [Theory]
    [InlineData(2026, 1, 1, 1, 15)]
    [InlineData(2026, 1, 2, 16, 31)]
    [InlineData(2026, 4, 2, 16, 30)]
    [InlineData(2026, 2, 2, 16, 28)]
    [InlineData(2028, 2, 2, 16, 29)]
    [InlineData(2026, 12, 2, 16, 31)]
    public void CutoffPeriodBuildsExpectedDateRanges(int year, int month, int cutoffNumber, int expectedStartDay, int expectedEndDay)
    {
        var cutoff = CutoffPeriod.Create(year, month, cutoffNumber);

        Assert.Equal(new DateOnly(year, month, expectedStartDay), cutoff.StartDate);
        Assert.Equal(new DateOnly(year, month, expectedEndDay), cutoff.EndDate);
        Assert.Equal($"{year:0000}-{month:00}-{cutoffNumber}", cutoff.Key);
    }

    [Fact]
    public void AllMonthsHaveTwoCutoffs()
    {
        var cutoffs = Enumerable.Range(1, 12)
            .SelectMany(month => BioAttendanceOrganizer.Core.Services.CutoffService.GenerateMonth(2026, month))
            .ToList();

        Assert.Equal(24, cutoffs.Count);
        Assert.All(Enumerable.Range(1, 12), month =>
        {
            Assert.Contains(cutoffs, x => x.Month == month && x.CutoffNumber == 1);
            Assert.Contains(cutoffs, x => x.Month == month && x.CutoffNumber == 2);
        });
    }
}

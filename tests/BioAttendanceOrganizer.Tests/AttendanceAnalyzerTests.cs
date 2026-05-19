using BioAttendanceOrganizer.Core.Analysis;
using BioAttendanceOrganizer.Core.Import;
using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.Tests;

public sealed class AttendanceAnalyzerTests
{
    private readonly AttendanceAnalyzer _analyzer = new();

    [Fact]
    public void PairsCleanDayShiftFromSameDayPunches()
    {
        var report = _analyzer.Analyze(Workbook(
            new DateTime(2026, 4, 1, 6, 0, 0),
            new DateTime(2026, 4, 1, 17, 5, 0)));

        var session = OnlyNonNoRecord(report);
        Assert.Equal(AttendanceStatus.CleanDayShift, session.Status);
        Assert.Equal(11.08, Math.Round(session.DurationHours!.Value, 2));
        Assert.Empty(session.Flags);
    }

    [Fact]
    public void PairsLikelyNightShiftAcrossTwoDates()
    {
        var report = _analyzer.Analyze(Workbook(
            new DateTime(2026, 4, 7, 23, 57, 0),
            new DateTime(2026, 4, 8, 6, 5, 0)));

        var session = OnlyNonNoRecord(report);
        Assert.Equal(AttendanceStatus.LikelyNightShift, session.Status);
        Assert.Contains(IssueFlag.LikelyNightShift, session.Flags);
        Assert.Equal(new DateOnly(2026, 4, 7), session.WorkDate);
        Assert.False(session.NeedsReview);
        Assert.Equal(CorrectionStatus.Auto, session.FinalCorrectionStatus);
        Assert.Equal("Night Shift", session.FinalStatusText);
    }

    [Fact]
    public void CleanNightShiftRequiresReviewWhenAutoApproveIsDisabled()
    {
        var report = _analyzer.Analyze(
            Workbook(
                new DateTime(2026, 4, 7, 23, 57, 0),
                new DateTime(2026, 4, 8, 6, 5, 0)),
            new AttendanceRules { AutoApproveCleanNightShifts = false });

        var session = OnlyNonNoRecord(report);
        Assert.Equal(AttendanceStatus.LikelyNightShift, session.Status);
        Assert.True(session.NeedsReview);
        Assert.Equal(CorrectionStatus.StillNeedsReview, session.FinalCorrectionStatus);
    }

    [Fact]
    public void NightShiftWithExtraPunchesIsReadyWhenTimePairIsRecognized()
    {
        var report = _analyzer.Analyze(Workbook(
            new DateTime(2026, 4, 7, 23, 57, 0),
            new DateTime(2026, 4, 8, 2, 0, 0),
            new DateTime(2026, 4, 8, 6, 5, 0)));

        var session = OnlyNonNoRecord(report);
        Assert.Equal(AttendanceStatus.LikelyNightShift, session.Status);
        Assert.Contains(IssueFlag.ExtraPunches, session.Flags);
        Assert.False(session.NeedsReview);
        Assert.Equal(0, report.Summary.IssueRows);
        Assert.Equal(1, report.Summary.ExtraPunchRows);
    }

    [Fact]
    public void SingleNightPunchStillRequiresReview()
    {
        var report = _analyzer.Analyze(Workbook(new DateTime(2026, 4, 7, 23, 57, 0)));

        var session = OnlyNonNoRecord(report);
        Assert.Equal(AttendanceStatus.MissingOut, session.Status);
        Assert.Contains(IssueFlag.MissingOut, session.Flags);
        Assert.True(session.NeedsReview);
    }

    [Fact]
    public void MarksFirstMorningPunchAsPreviousCutoffWhenNightPairFollows()
    {
        var report = _analyzer.Analyze(Workbook(
            new DateTime(2026, 4, 1, 6, 0, 0),
            new DateTime(2026, 4, 1, 18, 0, 0),
            new DateTime(2026, 4, 2, 6, 0, 0)));

        var rows = NonNoRecord(report).ToList();
        var carryover = Assert.Single(rows, x => x.Status == AttendanceStatus.CarryoverFromPreviousCutoff);
        Assert.False(carryover.NeedsReview);
        Assert.Equal(CorrectionStatus.Auto, carryover.FinalCorrectionStatus);
        Assert.True(carryover.IsInformationalPreviousCutoffCarryover);
        Assert.Contains(rows, x => x.Status == AttendanceStatus.LikelyNightShift);
        Assert.Equal(0, report.Summary.IssueRows);
        Assert.Equal(1, report.Summary.CarryoverRows);
    }

    [Fact]
    public void FlagsDuplicateTapWithoutInventingMissingOut()
    {
        var report = _analyzer.Analyze(Workbook(
            new DateTime(2026, 4, 2, 6, 0, 0),
            new DateTime(2026, 4, 2, 6, 1, 0)));

        var row = OnlyNonNoRecord(report);
        Assert.Equal(AttendanceStatus.MissingOut, row.Status);
        Assert.Contains(IssueFlag.DuplicateTap, row.Flags);
        Assert.Contains(IssueFlag.MissingOut, row.Flags);
        Assert.True(row.NeedsReview);
    }

    [Fact]
    public void DuplicateTapOnRecognizedDayShiftIsNotNeedsChecking()
    {
        var report = _analyzer.Analyze(Workbook(
            new DateTime(2026, 4, 2, 6, 0, 0),
            new DateTime(2026, 4, 2, 6, 1, 0),
            new DateTime(2026, 4, 2, 17, 5, 0)));

        var row = OnlyNonNoRecord(report);
        Assert.Equal(AttendanceStatus.CleanDayShift, row.Status);
        Assert.Contains(IssueFlag.DuplicateTap, row.Flags);
        Assert.False(row.NeedsReview);
        Assert.True(row.IsInformationalDuplicateTap);
        Assert.Equal(0, report.Summary.IssueRows);
        Assert.Equal(1, report.EmployeeSummaries.Single().DuplicateTapRows);
    }

    [Fact]
    public void UsesBestPairAndFlagsExtraPunches()
    {
        var report = _analyzer.Analyze(Workbook(
            new DateTime(2026, 4, 1, 6, 6, 0),
            new DateTime(2026, 4, 1, 11, 59, 0),
            new DateTime(2026, 4, 1, 17, 44, 0),
            new DateTime(2026, 4, 1, 18, 0, 0)));

        var row = OnlyNonNoRecord(report);
        Assert.Equal(AttendanceStatus.CleanDayShift, row.Status);
        Assert.Contains(IssueFlag.ExtraPunches, row.Flags);
        Assert.Equal(new DateTime(2026, 4, 1, 6, 6, 0), row.TimeIn);
        Assert.Equal(new DateTime(2026, 4, 1, 18, 0, 0), row.TimeOut);
        Assert.False(row.NeedsReview);
        Assert.True(row.IsInformationalExtraPunches);
        Assert.Equal(0, report.Summary.IssueRows);
        Assert.Equal(1, report.Summary.ExtraPunchRows);
    }

    [Fact]
    public void MarksLastStrongNightPunchAsNextCutoffCarryover()
    {
        var report = _analyzer.Analyze(Workbook(new DateTime(2026, 4, 16, 23, 55, 0)));

        var row = OnlyNonNoRecord(report);
        Assert.Equal(AttendanceStatus.CarryoverToNextCutoff, row.Status);
        Assert.Contains(IssueFlag.Carryover, row.Flags);
        Assert.True(row.NeedsReview);
        Assert.Equal(1, report.Summary.IssueRows);
        Assert.Equal(1, report.Summary.CarryoverRows);
    }

    [Fact]
    public void ParsesWorkbookPeriodInsteadOfFilenameRange()
    {
        var period = BiometricWorkbookParser.TryParsePeriodText("2026/04/01 ~ 04/16\t( Aaron Eagle )");

        Assert.NotNull(period);
        Assert.Equal(new DateOnly(2026, 4, 1), period.Value.Start);
        Assert.Equal(new DateOnly(2026, 4, 16), period.Value.End);
    }

    private static BiometricWorkbook Workbook(params DateTime[] punches)
    {
        var employee = new EmployeeInfo("1", "Sample Employee", "SECURITY");
        var rawPunches = punches
            .Select((timestamp, index) => new RawPunch(
                employee.Id,
                employee.Name,
                employee.Department,
                timestamp,
                timestamp.Day,
                timestamp.ToString("HH:mm"),
                "Logs",
                index + 1,
                timestamp.Day))
            .ToList();

        return new BiometricWorkbook(
            "sample.xls",
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 16),
            new[] { employee },
            rawPunches);
    }

    private static AttendanceRecord OnlyNonNoRecord(AttendanceReport report)
    {
        return Assert.Single(NonNoRecord(report));
    }

    private static IEnumerable<AttendanceRecord> NonNoRecord(AttendanceReport report)
    {
        return report.Records.Where(x => x.Status != AttendanceStatus.NoRecord);
    }
}

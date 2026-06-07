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
            DtrImportSlot.Morning,
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
    public void NightImportPairsLateNextDayOutAfterStrongEveningIn()
    {
        var report = _analyzer.Analyze(
            Workbook(
                DtrImportSlot.Night,
                new DateOnly(2026, 5, 16),
                new DateOnly(2026, 6, 1),
                new DateTime(2026, 5, 25, 17, 7, 0),
                new DateTime(2026, 5, 26, 6, 37, 0),
                new DateTime(2026, 5, 26, 16, 54, 0),
                new DateTime(2026, 5, 27, 6, 4, 0),
                new DateTime(2026, 5, 27, 17, 1, 0),
                new DateTime(2026, 5, 28, 12, 1, 0),
                new DateTime(2026, 5, 28, 17, 13, 0),
                new DateTime(2026, 5, 29, 7, 27, 0)));

        var rows = NonNoRecord(report).ToList();
        var may27 = Assert.Single(rows, x => x.WorkDate == new DateOnly(2026, 5, 27));
        Assert.Equal(AttendanceStatus.LikelyNightShift, may27.Status);
        Assert.Equal(new DateTime(2026, 5, 27, 17, 1, 0), may27.TimeIn);
        Assert.Equal(new DateTime(2026, 5, 28, 12, 1, 0), may27.TimeOut);
        Assert.Equal(19, Math.Round(may27.DurationHours!.Value, 2));

        var may28 = Assert.Single(rows, x => x.WorkDate == new DateOnly(2026, 5, 28));
        Assert.Equal(AttendanceStatus.LikelyNightShift, may28.Status);
        Assert.Equal(new DateTime(2026, 5, 28, 17, 13, 0), may28.TimeIn);
        Assert.Equal(new DateTime(2026, 5, 29, 7, 27, 0), may28.TimeOut);
        Assert.Equal(14.23, Math.Round(may28.DurationHours!.Value, 2));
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
    public void NightImportMarksFirstMorningPunchAsPreviousCutoffWhenNightPairFollows()
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
    public void MorningImportDoesNotMarkFirstMorningPunchAsPreviousCutoffWhenNightPairFollows()
    {
        var report = _analyzer.Analyze(Workbook(
            DtrImportSlot.Morning,
            new DateTime(2026, 4, 1, 6, 0, 0),
            new DateTime(2026, 4, 1, 18, 0, 0),
            new DateTime(2026, 4, 2, 6, 0, 0)));

        var rows = NonNoRecord(report).ToList();
        Assert.DoesNotContain(rows, x => x.Status == AttendanceStatus.CarryoverFromPreviousCutoff);
        Assert.DoesNotContain(rows, x => x.Flags.Contains(IssueFlag.Carryover));
        Assert.Equal(0, report.Summary.CarryoverRows);
    }

    [Fact]
    public void MorningImportDoesNotRecognizeNightPair()
    {
        var report = _analyzer.Analyze(Workbook(
            DtrImportSlot.Morning,
            new DateTime(2026, 4, 1, 18, 0, 0),
            new DateTime(2026, 4, 2, 6, 0, 0)));

        var rows = NonNoRecord(report).ToList();
        Assert.DoesNotContain(rows, x => x.Status == AttendanceStatus.LikelyNightShift);
        Assert.DoesNotContain(rows, x => x.Flags.Contains(IssueFlag.LikelyNightShift));
    }

    [Fact]
    public void NightImportDoesNotRecognizeDayPair()
    {
        var report = _analyzer.Analyze(Workbook(
            DtrImportSlot.Night,
            new DateTime(2026, 4, 1, 6, 0, 0),
            new DateTime(2026, 4, 1, 17, 5, 0)));

        var rows = NonNoRecord(report).ToList();
        Assert.DoesNotContain(rows, x => x.Status == AttendanceStatus.CleanDayShift);
    }

    [Fact]
    public void MorningImportPairsCrossMidnightOutWhenDayEndWindowWraps()
    {
        var report = _analyzer.Analyze(
            Workbook(
                DtrImportSlot.Morning,
                new DateTime(2026, 5, 24, 5, 8, 0),
                new DateTime(2026, 5, 25, 0, 3, 0),
                new DateTime(2026, 5, 25, 5, 53, 0)),
            new AttendanceRules
            {
                DayEndStart = new TimeSpan(12, 0, 0),
                DayEndEnd = new TimeSpan(4, 0, 0)
            });

        var rows = NonNoRecord(report).ToList();
        var may24 = Assert.Single(rows, x => x.WorkDate == new DateOnly(2026, 5, 24));
        Assert.Equal(AttendanceStatus.CleanDayShift, may24.Status);
        Assert.Equal(new DateTime(2026, 5, 24, 5, 8, 0), may24.TimeIn);
        Assert.Equal(new DateTime(2026, 5, 25, 0, 3, 0), may24.TimeOut);

        var may25 = Assert.Single(rows, x => x.WorkDate == new DateOnly(2026, 5, 25));
        Assert.Equal(AttendanceStatus.MissingOut, may25.Status);
        Assert.Equal(new DateTime(2026, 5, 25, 5, 53, 0), may25.TimeIn);
    }

    [Fact]
    public void MorningImportPairsCurrentDayAfterPreviousDayCrossMidnightOut()
    {
        var report = _analyzer.Analyze(
            Workbook(
                DtrImportSlot.Morning,
                new DateOnly(2026, 5, 16),
                new DateOnly(2026, 5, 31),
                new DateTime(2026, 5, 17, 5, 13, 0),
                new DateTime(2026, 5, 18, 0, 5, 0),
                new DateTime(2026, 5, 18, 5, 8, 0),
                new DateTime(2026, 5, 18, 19, 14, 0)));

        var rows = NonNoRecord(report).ToList();
        var may17 = Assert.Single(rows, x => x.WorkDate == new DateOnly(2026, 5, 17));
        Assert.Equal(AttendanceStatus.CleanDayShift, may17.Status);
        Assert.Equal(new DateTime(2026, 5, 17, 5, 13, 0), may17.TimeIn);
        Assert.Equal(new DateTime(2026, 5, 18, 0, 5, 0), may17.TimeOut);
        Assert.Equal(18.87, Math.Round(may17.DurationHours!.Value, 2));

        var may18 = Assert.Single(rows, x => x.WorkDate == new DateOnly(2026, 5, 18));
        Assert.Equal(AttendanceStatus.CleanDayShift, may18.Status);
        Assert.Equal(new DateTime(2026, 5, 18, 5, 8, 0), may18.TimeIn);
        Assert.Equal(new DateTime(2026, 5, 18, 19, 14, 0), may18.TimeOut);
        Assert.Equal(14.1, Math.Round(may18.DurationHours!.Value, 2));
    }

    [Fact]
    public void MorningImportKeepsLateNightPunchForNextDayWhenEveningOutExists()
    {
        var report = _analyzer.Analyze(
            Workbook(
                DtrImportSlot.Morning,
                new DateOnly(2026, 5, 16),
                new DateOnly(2026, 5, 31),
                new DateTime(2026, 5, 18, 5, 7, 0),
                new DateTime(2026, 5, 18, 19, 13, 0),
                new DateTime(2026, 5, 18, 23, 55, 0),
                new DateTime(2026, 5, 18, 23, 57, 0),
                new DateTime(2026, 5, 19, 18, 6, 0)));

        var rows = NonNoRecord(report).ToList();
        var may18 = Assert.Single(rows, x => x.WorkDate == new DateOnly(2026, 5, 18));
        Assert.Equal(AttendanceStatus.CleanDayShift, may18.Status);
        Assert.Equal(new DateTime(2026, 5, 18, 5, 7, 0), may18.TimeIn);
        Assert.Equal(new DateTime(2026, 5, 18, 19, 13, 0), may18.TimeOut);
        Assert.Equal(14.1, Math.Round(may18.DurationHours!.Value, 2));

        var may19 = Assert.Single(rows, x => x.WorkDate == new DateOnly(2026, 5, 19));
        Assert.Equal(AttendanceStatus.CleanDayShift, may19.Status);
        Assert.Equal(new DateTime(2026, 5, 18, 23, 55, 0), may19.TimeIn);
        Assert.Equal(new DateTime(2026, 5, 19, 18, 6, 0), may19.TimeOut);
        Assert.Equal(18.18, Math.Round(may19.DurationHours!.Value, 2));
        Assert.Contains(IssueFlag.DuplicateTap, may19.Flags);
    }

    [Fact]
    public void MorningImportKeepsAfterMidnightPunchForCurrentDayWhenPreviousEveningOutExists()
    {
        var report = _analyzer.Analyze(
            Workbook(
                DtrImportSlot.Morning,
                new DateOnly(2026, 5, 16),
                new DateOnly(2026, 5, 31),
                new DateTime(2026, 5, 18, 5, 7, 0),
                new DateTime(2026, 5, 18, 18, 41, 0),
                new DateTime(2026, 5, 19, 0, 26, 0),
                new DateTime(2026, 5, 19, 18, 0, 0)));

        var rows = NonNoRecord(report).ToList();
        var may18 = Assert.Single(rows, x => x.WorkDate == new DateOnly(2026, 5, 18));
        Assert.Equal(AttendanceStatus.CleanDayShift, may18.Status);
        Assert.Equal(new DateTime(2026, 5, 18, 5, 7, 0), may18.TimeIn);
        Assert.Equal(new DateTime(2026, 5, 18, 18, 41, 0), may18.TimeOut);

        var may19 = Assert.Single(rows, x => x.WorkDate == new DateOnly(2026, 5, 19));
        Assert.Equal(AttendanceStatus.CleanDayShift, may19.Status);
        Assert.Equal(new DateTime(2026, 5, 19, 0, 26, 0), may19.TimeIn);
        Assert.Equal(new DateTime(2026, 5, 19, 18, 0, 0), may19.TimeOut);
    }

    [Fact]
    public void MorningImportDoesNotPairCrossMidnightOutWhenDayEndWindowDoesNotWrap()
    {
        var report = _analyzer.Analyze(
            Workbook(
                DtrImportSlot.Morning,
                new DateTime(2026, 5, 24, 5, 8, 0),
                new DateTime(2026, 5, 25, 0, 3, 0),
                new DateTime(2026, 5, 25, 5, 53, 0)),
            new AttendanceRules
            {
                DayEndStart = new TimeSpan(12, 0, 0),
                DayEndEnd = new TimeSpan(20, 0, 0)
            });

        var rows = NonNoRecord(report).ToList();
        Assert.DoesNotContain(rows, x =>
            x.Status == AttendanceStatus.CleanDayShift &&
            x.TimeIn == new DateTime(2026, 5, 24, 5, 8, 0) &&
            x.TimeOut == new DateTime(2026, 5, 25, 0, 3, 0));
    }

    [Fact]
    public void NoRecordRowsStayWithinImportedWorkbookPeriod()
    {
        var report = _analyzer.Analyze(Workbook(
            DtrImportSlot.Morning,
            new DateOnly(2026, 5, 24),
            new DateOnly(2026, 5, 27),
            new DateTime(2026, 5, 24, 5, 8, 0),
            new DateTime(2026, 5, 24, 18, 0, 0)));

        var noRecordDates = report.Records
            .Where(x => x.Status == AttendanceStatus.NoRecord)
            .Select(x => x.WorkDate)
            .ToList();

        Assert.Equal([new DateOnly(2026, 5, 25), new DateOnly(2026, 5, 26), new DateOnly(2026, 5, 27)], noRecordDates);
        Assert.DoesNotContain(noRecordDates, x => x < new DateOnly(2026, 5, 24) || x > new DateOnly(2026, 5, 27));
    }

    [Fact]
    public void RawPunchDateUsedAsPreviousDayOutSuppressesNoRecordForThatDate()
    {
        var report = _analyzer.Analyze(
            Workbook(
                DtrImportSlot.Morning,
                new DateOnly(2026, 5, 24),
                new DateOnly(2026, 5, 27),
                new DateTime(2026, 5, 24, 5, 8, 0),
                new DateTime(2026, 5, 25, 0, 3, 0)),
            new AttendanceRules
            {
                DayEndStart = new TimeSpan(12, 0, 0),
                DayEndEnd = new TimeSpan(4, 0, 0)
            });

        Assert.Contains(report.Records, x =>
            x.WorkDate == new DateOnly(2026, 5, 24) &&
            x.Status == AttendanceStatus.CleanDayShift &&
            x.TimeOut == new DateTime(2026, 5, 25, 0, 3, 0));
        Assert.DoesNotContain(report.Records, x =>
            x.WorkDate == new DateOnly(2026, 5, 25) &&
            x.Status == AttendanceStatus.NoRecord);
    }

    [Fact]
    public void FlagsDuplicateTapWithoutInventingMissingOut()
    {
        var report = _analyzer.Analyze(Workbook(
            DtrImportSlot.Morning,
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
            DtrImportSlot.Morning,
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
            DtrImportSlot.Morning,
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
        return Workbook(DtrImportSlot.Night, punches);
    }

    private static BiometricWorkbook Workbook(DtrImportSlot slot, params DateTime[] punches)
    {
        return Workbook(slot, new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 16), punches);
    }

    private static BiometricWorkbook Workbook(
        DtrImportSlot slot,
        DateOnly periodStart,
        DateOnly periodEnd,
        params DateTime[] punches)
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
            periodStart,
            periodEnd,
            new[] { employee },
            rawPunches,
            slot);
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

using BioAttendanceOrganizer.Core.Models;
using BioAttendanceOrganizer.Core.Review;

namespace BioAttendanceOrganizer.Tests;

public sealed class CorrectionServiceTests
{
    private static readonly AttendanceRules Rules = new();

    [Fact]
    public void ApproveAsCorrectPreservesRecognizedTimesAndStatus()
    {
        var timeIn = new DateTime(2026, 4, 1, 8, 0, 0);
        var timeOut = new DateTime(2026, 4, 1, 17, 0, 0);
        var record = Record(timeIn, timeOut, AttendanceStatus.CleanDayShift);

        CorrectionService.Apply(record, CorrectionAction.ApproveAsCorrect, null, null, "Looks right");

        Assert.Equal(CorrectionAction.ApproveAsCorrect, record.CorrectionAction);
        Assert.Equal(timeIn, record.CorrectedTimeIn);
        Assert.Equal(timeOut, record.CorrectedTimeOut);
        Assert.Equal(AttendanceStatus.CleanDayShift, record.CorrectedStatus);
        Assert.Equal("Looks right", record.ReviewerNote);
    }

    [Fact]
    public void MarkMissingInKeepsAvailableOutAndSetsMissingIn()
    {
        var timeOut = new DateTime(2026, 4, 1, 17, 0, 0);
        var record = Record(null, timeOut, AttendanceStatus.MissingIn);

        CorrectionService.Apply(record, CorrectionAction.MarkMissingIn, null, timeOut, "No IN tap");

        Assert.Equal(CorrectionAction.MarkMissingIn, record.CorrectionAction);
        Assert.Null(record.CorrectedTimeIn);
        Assert.Equal(timeOut, record.CorrectedTimeOut);
        Assert.Equal(AttendanceStatus.MissingIn, record.CorrectedStatus);
    }

    [Fact]
    public void MarkMissingOutKeepsAvailableInAndSetsMissingOut()
    {
        var timeIn = new DateTime(2026, 4, 1, 8, 0, 0);
        var record = Record(timeIn, null, AttendanceStatus.MissingOut);

        CorrectionService.Apply(record, CorrectionAction.MarkMissingOut, timeIn, null, "No OUT tap");

        Assert.Equal(CorrectionAction.MarkMissingOut, record.CorrectionAction);
        Assert.Equal(timeIn, record.CorrectedTimeIn);
        Assert.Null(record.CorrectedTimeOut);
        Assert.Equal(AttendanceStatus.MissingOut, record.CorrectedStatus);
    }

    [Fact]
    public void MarkNeedsReviewSetsNeedsReviewStatus()
    {
        var timeIn = new DateTime(2026, 4, 1, 8, 0, 0);
        var timeOut = new DateTime(2026, 4, 1, 17, 0, 0);
        var record = Record(timeIn, timeOut, AttendanceStatus.CleanDayShift);

        CorrectionService.Apply(record, CorrectionAction.MarkNeedsReview, timeIn, timeOut, "Double-check");

        Assert.Equal(CorrectionAction.MarkNeedsReview, record.CorrectionAction);
        Assert.Equal(timeIn, record.CorrectedTimeIn);
        Assert.Equal(timeOut, record.CorrectedTimeOut);
        Assert.Equal(AttendanceStatus.NeedsReview, record.CorrectedStatus);
    }

    [Fact]
    public void InlineEditInfersDayShiftWithHours()
    {
        var record = Record(null, null, AttendanceStatus.NeedsReview);
        var timeIn = new DateTime(2026, 4, 1, 8, 0, 0);
        var timeOut = new DateTime(2026, 4, 1, 17, 0, 0);

        CorrectionService.ApplyInlineEdit(record, timeIn, timeOut, Rules, string.Empty);

        Assert.Equal(CorrectionAction.EditTimes, record.CorrectionAction);
        Assert.Equal(AttendanceStatus.CleanDayShift, record.FinalStatus);
        Assert.Equal(9, record.DurationHours.GetValueOrDefault(), precision: 2);
    }

    [Fact]
    public void InlineEditInfersNightShiftWithHours()
    {
        var record = Record(null, null, AttendanceStatus.NeedsReview);
        var timeIn = new DateTime(2026, 4, 1, 20, 0, 0);
        var timeOut = new DateTime(2026, 4, 2, 6, 0, 0);

        CorrectionService.ApplyInlineEdit(record, timeIn, timeOut, Rules, string.Empty);

        Assert.Equal(AttendanceStatus.LikelyNightShift, record.FinalStatus);
        Assert.Equal(10, record.DurationHours.GetValueOrDefault(), precision: 2);
    }

    [Fact]
    public void InlineEditInfersMissingSides()
    {
        var originalTimeIn = new DateTime(2026, 4, 1, 8, 0, 0);
        var originalTimeOut = new DateTime(2026, 4, 1, 17, 0, 0);
        var missingIn = Record(originalTimeIn, originalTimeOut, AttendanceStatus.CleanDayShift);
        var missingOut = Record(originalTimeIn, originalTimeOut, AttendanceStatus.CleanDayShift);
        var timeIn = new DateTime(2026, 4, 1, 8, 0, 0);
        var timeOut = new DateTime(2026, 4, 1, 17, 0, 0);

        CorrectionService.ApplyInlineEdit(missingIn, null, timeOut, Rules, string.Empty);
        CorrectionService.ApplyInlineEdit(missingOut, timeIn, null, Rules, string.Empty);

        Assert.Null(missingIn.FinalTimeIn);
        Assert.Equal(timeOut, missingIn.FinalTimeOut);
        Assert.Equal(timeIn, missingOut.FinalTimeIn);
        Assert.Null(missingOut.FinalTimeOut);
        Assert.Equal(AttendanceStatus.MissingIn, missingIn.FinalStatus);
        Assert.Equal(AttendanceStatus.MissingOut, missingOut.FinalStatus);
        Assert.Contains(IssueFlag.MissingIn, CorrectionService.FlagsForStatus(missingIn.FinalStatus));
        Assert.Contains(IssueFlag.MissingOut, CorrectionService.FlagsForStatus(missingOut.FinalStatus));
    }

    [Fact]
    public void InlineEditRejectsOutBeforeIn()
    {
        var record = Record(null, null, AttendanceStatus.NeedsReview);
        var timeIn = new DateTime(2026, 4, 1, 17, 0, 0);
        var timeOut = new DateTime(2026, 4, 1, 8, 0, 0);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CorrectionService.ApplyInlineEdit(record, timeIn, timeOut, Rules, string.Empty));

        Assert.Contains("Time Out must be after Time In", ex.Message);
    }

    [Fact]
    public void InlineEditInfersTooShort()
    {
        var record = Record(null, null, AttendanceStatus.NeedsReview);
        var timeIn = new DateTime(2026, 4, 1, 8, 0, 0);
        var timeOut = new DateTime(2026, 4, 1, 9, 0, 0);

        CorrectionService.ApplyInlineEdit(record, timeIn, timeOut, Rules, string.Empty);

        Assert.Equal(AttendanceStatus.TooShort, record.FinalStatus);
        Assert.Contains(IssueFlag.TooShort, CorrectionService.FlagsForStatus(record.FinalStatus));
    }

    private static AttendanceRecord Record(DateTime? timeIn, DateTime? timeOut, AttendanceStatus status)
    {
        return new AttendanceRecord
        {
            EmployeeId = "1",
            EmployeeName = "Sample Employee",
            Department = "SECURITY",
            WorkDate = new DateOnly(2026, 4, 1),
            TimeIn = timeIn,
            TimeOut = timeOut,
            Status = status,
            Confidence = 1,
            RawPunches = string.Empty
        };
    }
}

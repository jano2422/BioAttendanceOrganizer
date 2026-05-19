using BioAttendanceOrganizer.Core.Models;
using BioAttendanceOrganizer.Core.Services;

namespace BioAttendanceOrganizer.Tests;

public sealed class DtrImportWorkflowTests
{
    [Fact]
    public void InferCutoffSelectsExactFirstCutoff()
    {
        var result = new DtrImportWorkflowService().InferCutoff(
            Workbook(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 15)),
            currentYear: 2026);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Cutoff);
        Assert.Equal(2026, result.Cutoff.Year);
        Assert.Equal(4, result.Cutoff.Month);
        Assert.Equal(1, result.Cutoff.CutoffNumber);
    }

    [Fact]
    public void InferCutoffSelectsExactSecondCutoff()
    {
        var result = new DtrImportWorkflowService().InferCutoff(
            Workbook(new DateOnly(2026, 4, 16), new DateOnly(2026, 4, 30)),
            currentYear: 2026);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Cutoff);
        Assert.Equal(2026, result.Cutoff.Year);
        Assert.Equal(4, result.Cutoff.Month);
        Assert.Equal(2, result.Cutoff.CutoffNumber);
    }

    [Fact]
    public void InferCutoffAllowsPreviousDayBoundaryOverlap()
    {
        var result = new DtrImportWorkflowService().InferCutoff(
            Workbook(new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 15)),
            currentYear: 2026);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Cutoff);
        Assert.Equal(new DateOnly(2026, 4, 1), result.Cutoff.StartDate);
        Assert.Equal(new DateOnly(2026, 4, 15), result.Cutoff.EndDate);
    }

    [Fact]
    public void InferCutoffAllowsNextDayBoundaryOverlap()
    {
        var result = new DtrImportWorkflowService().InferCutoff(
            Workbook(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 16)),
            currentYear: 2026);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Cutoff);
        Assert.Equal(new DateOnly(2026, 4, 1), result.Cutoff.StartDate);
        Assert.Equal(new DateOnly(2026, 4, 15), result.Cutoff.EndDate);
    }

    [Fact]
    public void InferCutoffAllowsPastYear()
    {
        var result = new DtrImportWorkflowService().InferCutoff(
            Workbook(new DateOnly(2024, 1, 16), new DateOnly(2024, 1, 31)),
            currentYear: 2026);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Cutoff);
        Assert.Equal(2024, result.Cutoff.Year);
        Assert.Equal(1, result.Cutoff.Month);
        Assert.Equal(2, result.Cutoff.CutoffNumber);
    }

    [Fact]
    public void InferCutoffRejectsFutureYear()
    {
        var result = new DtrImportWorkflowService().InferCutoff(
            Workbook(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 15)),
            currentYear: 2026);

        Assert.False(result.IsValid);
        Assert.Null(result.Cutoff);
        Assert.Contains("Future", result.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InferCutoffRejectsNoMatchingCutoff()
    {
        var result = new DtrImportWorkflowService().InferCutoff(
            Workbook(new DateOnly(2026, 4, 2), new DateOnly(2026, 4, 15)),
            currentYear: 2026);

        Assert.False(result.IsValid);
        Assert.Null(result.Cutoff);
        Assert.Contains("does not match", result.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MorningInferCutoffAllowsPartialOverlap()
    {
        var result = new DtrImportWorkflowService().InferCutoff(
            Workbook(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 9)),
            currentYear: 2026,
            slot: DtrImportSlot.Morning);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Cutoff);
        Assert.Equal(new DateOnly(2026, 5, 1), result.Cutoff.StartDate);
        Assert.Equal(new DateOnly(2026, 5, 15), result.Cutoff.EndDate);
    }

    [Fact]
    public void ImportValidationRequiresSelectedCutoff()
    {
        var result = new DtrImportWorkflowService().ValidateDtrOnly(
            Workbook(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 15)),
            null);

        Assert.False(result.IsValid);
        Assert.Contains("cutoff", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportValidationAllowsNextDayNightShiftBoundaryOverlap()
    {
        var result = new DtrImportWorkflowService().ValidateDtrOnly(
            Workbook(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 16)),
            CutoffPeriod.Create(2026, 4, 1));

        Assert.True(result.IsValid);
        Assert.Contains("boundary overlap", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DtrOnlyImportValidationDoesNotRequireMasterEmployees()
    {
        var result = new DtrImportWorkflowService().ValidateDtrOnly(
            Workbook(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 15)),
            CutoffPeriod.Create(2026, 4, 1));

        Assert.True(result.IsValid);
        Assert.Equal(2, result.EmployeeCount);
        Assert.Equal(1, result.RawPunchCount);
        Assert.Equal(0, result.UnknownEmployeeCount);
        Assert.Equal(0, result.ConflictCount);
    }

    [Fact]
    public void DtrOnlyImportValidationUsesSelectedCutoffTilePeriod()
    {
        var result = new DtrImportWorkflowService().ValidateDtrOnly(
            Workbook(new DateOnly(2026, 4, 16), new DateOnly(2026, 4, 30)),
            CutoffPeriod.Create(2026, 4, 1));

        Assert.False(result.IsValid);
        Assert.Contains("does not match", result.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportValidationAllowsPreviousDayNightShiftBoundaryOverlap()
    {
        var result = new DtrImportWorkflowService().ValidateDtrOnly(
            Workbook(new DateOnly(2026, 3, 31), new DateOnly(2026, 4, 15)),
            CutoffPeriod.Create(2026, 4, 1));

        Assert.True(result.IsValid);
        Assert.Contains("boundary overlap", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MorningImportValidationAllowsPartialOverlap()
    {
        var result = new DtrImportWorkflowService().ValidateDtrOnly(
            Workbook(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 9)),
            CutoffPeriod.Create(2026, 5, 1),
            DtrImportSlot.Morning);

        Assert.True(result.IsValid);
        Assert.Contains("imported period only", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NightImportValidationRejectsPartialMorningPeriod()
    {
        var result = new DtrImportWorkflowService().ValidateDtrOnly(
            Workbook(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 9)),
            CutoffPeriod.Create(2026, 5, 1),
            DtrImportSlot.Night);

        Assert.False(result.IsValid);
        Assert.Contains("does not match", result.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MorningImportValidationRejectsNoOverlap()
    {
        var result = new DtrImportWorkflowService().ValidateDtrOnly(
            Workbook(new DateOnly(2026, 5, 16), new DateOnly(2026, 5, 31)),
            CutoffPeriod.Create(2026, 5, 1),
            DtrImportSlot.Morning);

        Assert.False(result.IsValid);
        Assert.Contains("does not match", result.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImportValidationRejectsWorkbookPeriodMismatchBeyondNightShiftBoundary()
    {
        var result = new DtrImportWorkflowService().ValidateDtrOnly(
            Workbook(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 17)),
            CutoffPeriod.Create(2026, 4, 1));

        Assert.False(result.IsValid);
        Assert.Contains("does not match", result.Title, StringComparison.OrdinalIgnoreCase);
    }

    private static BiometricWorkbook Workbook(DateOnly start, DateOnly end)
    {
        var employees = new[]
        {
            new EmployeeInfo("1", "Cut Name", "Wrong Gate"),
            new EmployeeInfo("2", "New Employee", "Gate 2")
        };
        var punches = new[]
        {
            new RawPunch("1", "Cut Name", "Wrong Gate", start.ToDateTime(new TimeOnly(6, 0)), start.Day, "06:00", "Logs", 1, 1)
        };

        return new BiometricWorkbook("sample.xls", start, end, employees, punches);
    }
}

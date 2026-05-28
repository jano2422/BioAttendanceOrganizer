using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.Tests;

public sealed class EmployeeAttendanceSummaryTests
{
    [Fact]
    public void NeedsCheckingEmployeeCountCountsEmployeeOnce()
    {
        var records = new[]
        {
            Record("1", "Needs Employee", new DateOnly(2026, 5, 1), AttendanceStatus.MissingIn),
            Record("1", "Needs Employee", new DateOnly(2026, 5, 2), AttendanceStatus.MissingOut),
            Record("2", "Ready Employee", new DateOnly(2026, 5, 1), AttendanceStatus.CleanDayShift)
        };

        var summaries = EmployeeAttendanceSummary.FromRecords(records);

        Assert.Equal(1, summaries.Count(x => x.IssueRows > 0));
        Assert.Equal(1, summaries.Count(x => x.IssueRows == 0));
    }

    [Fact]
    public void ReadyEmployeeCountIncludesReadyNightShiftEmployee()
    {
        var records = new[]
        {
            Record(
                "1",
                "Night Employee",
                new DateOnly(2026, 5, 1),
                AttendanceStatus.LikelyNightShift,
                [IssueFlag.LikelyNightShift])
        };

        var summary = Assert.Single(EmployeeAttendanceSummary.FromRecords(records));

        Assert.Equal(0, summary.IssueRows);
        Assert.Equal(1, summary.NightSessions);
    }

    private static AttendanceRecord Record(
        string employeeId,
        string employeeName,
        DateOnly workDate,
        AttendanceStatus status,
        IReadOnlyList<IssueFlag>? flags = null)
    {
        return new AttendanceRecord
        {
            EmployeeId = employeeId,
            EmployeeName = employeeName,
            Department = "SECURITY",
            WorkDate = workDate,
            TimeIn = workDate.ToDateTime(new TimeOnly(6, 0)),
            TimeOut = workDate.ToDateTime(new TimeOnly(18, 0)),
            Status = status,
            Flags = flags ?? Array.Empty<IssueFlag>(),
            Confidence = 1,
            RawPunches = "raw"
        };
    }
}

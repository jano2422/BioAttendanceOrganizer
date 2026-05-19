namespace BioAttendanceOrganizer.Core.Models;

public sealed record EmployeeAttendanceSummary(
    string EmployeeId,
    string EmployeeName,
    string Department,
    int CleanSessions,
    int NightSessions,
    int MissingInRows,
    int MissingOutRows,
    int CarryoverRows,
    int DuplicateTapRows,
    int ExtraPunchRows,
    int IssueRows,
    int ReviewedRows,
    int NoRecordRows,
    double TotalHours)
{
    public static IReadOnlyList<EmployeeAttendanceSummary> FromRecords(IEnumerable<AttendanceRecord> records)
    {
        return records
            .GroupBy(x => new { x.EmployeeId, x.EmployeeName, x.Department })
            .OrderBy(x => TryParseEmployeeId(x.Key.EmployeeId))
            .ThenBy(x => x.Key.EmployeeName)
            .Select(group => new EmployeeAttendanceSummary(
                group.Key.EmployeeId,
                group.Key.EmployeeName,
                group.Key.Department,
                group.Count(x => x.FinalStatus == AttendanceStatus.CleanDayShift && x.FinalCorrectionStatus != CorrectionStatus.StillNeedsReview),
                group.Count(x => x.FinalStatus == AttendanceStatus.LikelyNightShift),
                group.Count(x => x.FinalStatus == AttendanceStatus.MissingIn),
                group.Count(x => x.FinalStatus == AttendanceStatus.MissingOut),
                group.Count(x => x.FinalStatus is AttendanceStatus.CarryoverFromPreviousCutoff or AttendanceStatus.CarryoverToNextCutoff),
                group.Count(x => x.Flags.Contains(IssueFlag.DuplicateTap)),
                group.Count(x => x.Flags.Contains(IssueFlag.ExtraPunches)),
                group.Count(x => x.NeedsReview),
                group.Count(x => x.IsReviewed),
                group.Count(x => x.FinalStatus == AttendanceStatus.NoRecord),
                group.Sum(x => x.DurationHours ?? 0)))
            .ToList();
    }

    private static int TryParseEmployeeId(string employeeId)
    {
        return int.TryParse(employeeId, out var value) ? value : int.MaxValue;
    }
}

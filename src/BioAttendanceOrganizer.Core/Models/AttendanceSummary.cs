namespace BioAttendanceOrganizer.Core.Models;

public sealed record AttendanceSummary(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    int EmployeeCount,
    int RawPunchCount,
    int TotalRows,
    int CleanSessions,
    int NightSessions,
    int IssueRows,
    int NoRecordRows,
    int CarryoverRows,
    int MissingInRows,
    int MissingOutRows,
    int ExtraPunchRows)
{
    public static AttendanceSummary From(DateOnly start, DateOnly end, int employees, int rawPunches, IReadOnlyList<AttendanceRecord> records)
    {
        return new AttendanceSummary(
            start,
            end,
            employees,
            rawPunches,
            records.Count,
            records.Count(x => x.FinalStatus == AttendanceStatus.CleanDayShift && x.FinalCorrectionStatus != CorrectionStatus.StillNeedsReview),
            records.Count(x => x.FinalStatus == AttendanceStatus.LikelyNightShift),
            records.Count(x => x.NeedsReview),
            records.Count(x => x.FinalStatus == AttendanceStatus.NoRecord),
            records.Count(x => x.FinalStatus is AttendanceStatus.CarryoverFromPreviousCutoff or AttendanceStatus.CarryoverToNextCutoff),
            records.Count(x => x.FinalStatus == AttendanceStatus.MissingIn),
            records.Count(x => x.FinalStatus == AttendanceStatus.MissingOut),
            records.Count(x => x.Flags.Contains(IssueFlag.ExtraPunches)));
    }
}

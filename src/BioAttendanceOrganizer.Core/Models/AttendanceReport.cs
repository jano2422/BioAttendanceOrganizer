namespace BioAttendanceOrganizer.Core.Models;

public sealed record AttendanceReport(
    BiometricWorkbook Workbook,
    IReadOnlyList<AttendanceRecord> Records,
    AttendanceSummary Summary)
{
    public IReadOnlyList<EmployeeAttendanceSummary> EmployeeSummaries => EmployeeAttendanceSummary.FromRecords(Records);
}

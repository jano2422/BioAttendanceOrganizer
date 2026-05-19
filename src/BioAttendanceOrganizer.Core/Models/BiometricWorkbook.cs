namespace BioAttendanceOrganizer.Core.Models;

public sealed record BiometricWorkbook(
    string SourcePath,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<EmployeeInfo> Employees,
    IReadOnlyList<RawPunch> RawPunches,
    DtrImportSlot DetectedSlot = DtrImportSlot.Night);

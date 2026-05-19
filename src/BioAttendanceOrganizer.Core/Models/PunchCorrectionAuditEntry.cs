namespace BioAttendanceOrganizer.Core.Models;

public sealed record PunchCorrectionAuditEntry(
    long Id,
    string ImportKey,
    string CutoffKey,
    string SourcePunchKey,
    string EmployeeId,
    DateOnly WorkDate,
    PunchCorrectionAction Action,
    string BeforeValue,
    string AfterValue,
    string Reason,
    DateTime UpdatedAt);

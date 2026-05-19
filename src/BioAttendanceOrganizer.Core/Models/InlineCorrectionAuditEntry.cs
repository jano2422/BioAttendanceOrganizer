namespace BioAttendanceOrganizer.Core.Models;

public sealed record InlineCorrectionAuditEntry(
    long Id,
    string ImportKey,
    string RecordKey,
    string CutoffKey,
    string EmployeeId,
    DateOnly WorkDate,
    DateTime? FinalTimeIn,
    DateTime? FinalTimeOut,
    AttendanceStatus FinalStatus,
    string ReviewerNote,
    DateTime UpdatedAt);

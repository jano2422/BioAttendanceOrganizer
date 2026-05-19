namespace BioAttendanceOrganizer.Core.Models;

public sealed record CorrectionRecord(
    string ImportKey,
    string RecordKey,
    string EmployeeId,
    DateOnly WorkDate,
    string RawPunches,
    AttendanceStatus OriginalStatus,
    string OriginalFlags,
    DateTime? OriginalTimeIn,
    DateTime? OriginalTimeOut,
    CorrectionAction Action,
    AttendanceStatus? CorrectedStatus,
    DateTime? CorrectedTimeIn,
    DateTime? CorrectedTimeOut,
    string ReviewerNote,
    DateTime UpdatedAt);

namespace BioAttendanceOrganizer.Core.Models;

public sealed record PunchCorrectionRecord(
    string ImportKey,
    string SourcePunchKey,
    string OriginalEmployeeId,
    DateTime OriginalTimestamp,
    string EmployeeId,
    string EmployeeName,
    string Department,
    DateTime Timestamp,
    PunchType PunchType,
    string PairKey,
    bool IsDeleted,
    string Reason,
    DateTime UpdatedAt);

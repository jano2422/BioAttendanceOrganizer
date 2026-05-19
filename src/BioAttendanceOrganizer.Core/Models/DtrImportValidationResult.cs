namespace BioAttendanceOrganizer.Core.Models;

public sealed record DtrImportValidationResult(
    bool IsValid,
    string Title,
    string Message,
    int EmployeeCount,
    int RawPunchCount,
    int UnknownEmployeeCount,
    int ConflictCount)
{
    public static DtrImportValidationResult Invalid(string title, string message)
    {
        return new DtrImportValidationResult(false, title, message, 0, 0, 0, 0);
    }
}

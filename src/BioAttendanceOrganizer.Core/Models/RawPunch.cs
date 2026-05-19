using System.Globalization;

namespace BioAttendanceOrganizer.Core.Models;

public sealed record RawPunch(
    string EmployeeId,
    string EmployeeName,
    string Department,
    DateTime Timestamp,
    int SourceDay,
    string RawCell,
    string SourceSheet,
    int SourceRow,
    int SourceColumn)
{
    public string TimestampText => Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}

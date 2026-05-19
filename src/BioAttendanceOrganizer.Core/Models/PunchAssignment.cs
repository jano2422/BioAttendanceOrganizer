using System.Globalization;

namespace BioAttendanceOrganizer.Core.Models;

public sealed class PunchAssignment
{
    public string SourcePunchKey { get; init; } = string.Empty;
    public string OriginalEmployeeId { get; init; } = string.Empty;
    public string OriginalEmployeeName { get; init; } = string.Empty;
    public string OriginalDepartment { get; init; } = string.Empty;
    public DateTime OriginalTimestamp { get; init; }
    public string SourceSheet { get; init; } = string.Empty;
    public int SourceRow { get; init; }
    public int SourceColumn { get; init; }
    public string RawCell { get; init; } = string.Empty;
    public PunchType OriginalPunchType { get; set; }
    public string OriginalPairKey { get; set; } = string.Empty;
    public bool OriginalIsDeleted { get; set; }
    public string EmployeeId { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public PunchType PunchType { get; set; }
    public string PairKey { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public bool IsChanged =>
        IsDeleted ||
        EmployeeId != OriginalEmployeeId ||
        Timestamp != OriginalTimestamp ||
        PunchType != OriginalPunchType ||
        PairKey != OriginalPairKey ||
        IsDeleted != OriginalIsDeleted;

    public DateOnly WorkDate => DateOnly.FromDateTime(Timestamp);
    public string TimeText => Timestamp.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string TimestampText => Timestamp.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public PunchAssignment Clone()
    {
        return (PunchAssignment)MemberwiseClone();
    }
}

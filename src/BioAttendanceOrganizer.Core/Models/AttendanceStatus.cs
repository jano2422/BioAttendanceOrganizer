namespace BioAttendanceOrganizer.Core.Models;

public enum AttendanceStatus
{
    CleanDayShift,
    LikelyNightShift,
    NoRecord,
    MissingIn,
    MissingOut,
    CarryoverFromPreviousCutoff,
    CarryoverToNextCutoff,
    TooShort,
    NeedsReview
}

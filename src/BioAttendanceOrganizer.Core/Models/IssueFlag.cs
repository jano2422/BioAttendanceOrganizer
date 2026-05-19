namespace BioAttendanceOrganizer.Core.Models;

public enum IssueFlag
{
    LikelyNightShift,
    Carryover,
    MissingIn,
    MissingOut,
    ExtraPunches,
    DuplicateTap,
    TooShort,
    Ambiguous,
    BoundaryPunch,
    NeedsReview
}

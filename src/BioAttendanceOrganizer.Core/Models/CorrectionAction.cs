namespace BioAttendanceOrganizer.Core.Models;

public enum CorrectionAction
{
    None,
    ApproveAsCorrect,
    EditTimes,
    MarkMissingIn,
    MarkMissingOut,
    MarkCarryoverFromPreviousCutoff,
    MarkCarryoverToNextCutoff,
    IgnoreDuplicateTap,
    ApproveExtraPunches,
    MarkNeedsReview
}

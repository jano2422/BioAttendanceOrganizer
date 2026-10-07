using BioAttendanceOrganizer.Core.Models;
using BioAttendanceOrganizer.Core.Review;

namespace BioAttendanceOrganizer.App.ViewModels;

public sealed class EmployeeTimelineRow
{
    public EmployeeTimelineRow(AttendanceRecord record)
    {
        Record = record;
    }

    public AttendanceRecord Record { get; }
    public string EmployeeId => Record.EmployeeId;
    public string WorkDateText => Record.WorkDateText;
    public string ShiftRangeText => Record.ShiftRangeText;
    public bool IsOvernightShift => Record.IsOvernightShift;
    public bool IsCarryover => Record.FinalStatus == AttendanceStatus.CarryoverFromPreviousCutoff || Record.FinalStatus == AttendanceStatus.CarryoverToNextCutoff;
    public string CarryoverLabel => Record.FinalStatus == AttendanceStatus.CarryoverFromPreviousCutoff ? "From previous cutoff" : Record.FinalStatus == AttendanceStatus.CarryoverToNextCutoff ? "To next cutoff" : string.Empty;
    public string ShiftBadgeText => Record.FinalStatus == AttendanceStatus.LikelyNightShift ||
                                    Record.FinalStatus == AttendanceStatus.CarryoverFromPreviousCutoff ||
                                    Record.FinalStatus == AttendanceStatus.CarryoverToNextCutoff
        ? string.Empty
        : Record.IsOvernightShift
            ? "Extended"
            : string.Empty;
    public bool HasShiftBadge => !string.IsNullOrWhiteSpace(ShiftBadgeText);
    public string TimeInText => Record.TimeInText;
    public string TimeOutText => Record.TimeOutText;
    public string DurationText => Record.DurationText;
    public string DisplayTimeInText => Record.FinalTimeIn?.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "Missing IN";
    public string DisplayTimeOutText => Record.FinalTimeOut?.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "Missing OUT";
    public string DisplayDurationText => string.IsNullOrWhiteSpace(Record.DurationText) ? "-" : Record.DurationText;
    public string DisplayFlagText
    {
        get
        {
            var flags = Record.CorrectedStatus is null
                ? Record.Flags
                : CorrectionService.FlagsForStatus(Record.FinalStatus);
            return flags.Count == 0 ? "-" : string.Join(", ", flags.Select(x => x.ToString()));
        }
    }
    public string FinalStatusText => Record.FinalStatusText;
    public string CorrectionStatusText => Record.CorrectionStatusText;
    public string FlagText => Record.FlagText;
    public string RawPunches => Record.RawPunches;
    public string OriginalTimeInText => Record.OriginalTimeInText;
    public string OriginalTimeOutText => Record.OriginalTimeOutText;
    public string OriginalStatusText => Record.StatusText;
    public string CheckStatusText => Record.NeedsReview ? "Needs Checking" : "Ready";
    public bool NeedsChecking => Record.NeedsReview;
}

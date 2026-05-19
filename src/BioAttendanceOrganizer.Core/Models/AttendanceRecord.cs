using System.Globalization;

namespace BioAttendanceOrganizer.Core.Models;

public sealed class AttendanceRecord
{
    public string EmployeeId { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public string Department { get; init; } = string.Empty;
    public DateOnly WorkDate { get; init; }
    public DateTime? TimeIn { get; init; }
    public DateTime? TimeOut { get; init; }
    public AttendanceStatus Status { get; init; }
    public IReadOnlyList<IssueFlag> Flags { get; init; } = Array.Empty<IssueFlag>();
    public double Confidence { get; init; }
    public string RawPunches { get; init; } = string.Empty;
    public bool AutoApproveCleanNightShift { get; init; } = true;
    public string ReviewerNote { get; set; } = string.Empty;
    public CorrectionAction CorrectionAction { get; set; } = CorrectionAction.None;
    public AttendanceStatus? CorrectedStatus { get; set; }
    public DateTime? CorrectedTimeIn { get; set; }
    public DateTime? CorrectedTimeOut { get; set; }
    public DateTime? CorrectionUpdatedAt { get; set; }

    public DateTime? FinalTimeIn => UsesCorrectedTimes ? CorrectedTimeIn : TimeIn;
    public DateTime? FinalTimeOut => UsesCorrectedTimes ? CorrectedTimeOut : TimeOut;
    public AttendanceStatus FinalStatus => CorrectedStatus ?? Status;
    public TimeSpan? Duration => FinalTimeIn is null || FinalTimeOut is null ? null : FinalTimeOut.Value - FinalTimeIn.Value;
    public double? DurationHours => Duration?.TotalHours;
    public bool IsOvernightShift => FinalTimeIn.HasValue && FinalTimeOut.HasValue && FinalTimeOut.Value.Date > FinalTimeIn.Value.Date;
    public string ShiftRangeText
    {
        get
        {
            if (FinalTimeIn is null && FinalTimeOut is null) return string.Empty;
            var inStr = FinalTimeIn?.ToString(IsOvernightShift ? "MMM d HH:mm" : "HH:mm", CultureInfo.InvariantCulture) ?? "?";
            var outStr = FinalTimeOut?.ToString(IsOvernightShift ? "MMM d HH:mm" : "HH:mm", CultureInfo.InvariantCulture) ?? "?";
            return $"{inStr} → {outStr}";
        }
    }
    public string WorkDateText => WorkDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    public string TimeInText => FinalTimeIn?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
    public string TimeOutText => FinalTimeOut?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
    public string OriginalTimeInText => TimeIn?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
    public string OriginalTimeOutText => TimeOut?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
    public string DurationText => DurationHours?.ToString("0.00", CultureInfo.InvariantCulture) ?? string.Empty;
    public string FlagText => Flags.Count == 0 ? string.Empty : string.Join(", ", Flags.Select(x => x.ToString()));
    public string CorrectionStatusText => FinalCorrectionStatus.ToString();
    public string StatusText => FormatStatus(Status);
    public string FinalStatusText => FormatStatus(FinalStatus);
    public bool IsReviewed => CorrectionAction is not CorrectionAction.None and not CorrectionAction.MarkNeedsReview;
    public bool HasManualCorrection => CorrectionAction is CorrectionAction.EditTimes
        or CorrectionAction.MarkMissingIn
        or CorrectionAction.MarkMissingOut
        or CorrectionAction.MarkCarryoverFromPreviousCutoff
        or CorrectionAction.MarkCarryoverToNextCutoff;

    private bool UsesCorrectedTimes => CorrectionAction is not CorrectionAction.None;

    public CorrectionStatus FinalCorrectionStatus
    {
        get
        {
            if (CorrectionAction == CorrectionAction.MarkNeedsReview)
            {
                return CorrectionStatus.StillNeedsReview;
            }

            if (CorrectionAction == CorrectionAction.ApproveAsCorrect ||
                CorrectionAction == CorrectionAction.IgnoreDuplicateTap ||
                CorrectionAction == CorrectionAction.ApproveExtraPunches)
            {
                return CorrectionStatus.Approved;
            }

            if (HasManualCorrection)
            {
                return CorrectionStatus.ManualCorrection;
            }

            return NeedsReviewByAnalyzer ? CorrectionStatus.StillNeedsReview : CorrectionStatus.Auto;
        }
    }

    public bool IsCleanNightShift =>
        Status == AttendanceStatus.LikelyNightShift &&
        Flags.All(flag => flag is IssueFlag.LikelyNightShift or IssueFlag.DuplicateTap or IssueFlag.ExtraPunches);

    public bool IsInformationalDuplicateTap =>
        Status == AttendanceStatus.CleanDayShift &&
        Flags.Contains(IssueFlag.DuplicateTap) &&
        Flags.All(flag => flag is IssueFlag.DuplicateTap or IssueFlag.ExtraPunches);

    public bool IsInformationalExtraPunches =>
        Status == AttendanceStatus.CleanDayShift &&
        Flags.Contains(IssueFlag.ExtraPunches) &&
        Flags.All(flag => flag is IssueFlag.DuplicateTap or IssueFlag.ExtraPunches);

    public bool IsInformationalPreviousCutoffCarryover =>
        Status == AttendanceStatus.CarryoverFromPreviousCutoff &&
        Flags.All(flag => flag is IssueFlag.Carryover or IssueFlag.BoundaryPunch or IssueFlag.NeedsReview);

    public bool NeedsReviewByAnalyzer =>
        !(AutoApproveCleanNightShift && IsCleanNightShift) &&
        !IsInformationalPreviousCutoffCarryover &&
        !IsInformationalDuplicateTap &&
        !IsInformationalExtraPunches &&
        (Status is not AttendanceStatus.CleanDayShift and not AttendanceStatus.NoRecord ||
         Flags.Any(flag => flag is not IssueFlag.LikelyNightShift));

    public bool NeedsReview => FinalCorrectionStatus == CorrectionStatus.StillNeedsReview;

    private static string FormatStatus(AttendanceStatus status)
    {
        return status switch
        {
            AttendanceStatus.CleanDayShift => "Day Shift",
            AttendanceStatus.LikelyNightShift => "Night Shift",
            AttendanceStatus.NoRecord => "No Record",
            AttendanceStatus.MissingIn => "Missing In",
            AttendanceStatus.MissingOut => "Missing Out",
            AttendanceStatus.CarryoverFromPreviousCutoff => "Carryover From Previous Cutoff",
            AttendanceStatus.CarryoverToNextCutoff => "Carryover To Next Cutoff",
            AttendanceStatus.TooShort => "Too Short",
            AttendanceStatus.NeedsReview => "Needs Review",
            _ => status.ToString()
        };
    }
}

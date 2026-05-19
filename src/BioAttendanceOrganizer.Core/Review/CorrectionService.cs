using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.Core.Review;

public static class CorrectionService
{
    public sealed record CorrectionInference(AttendanceStatus Status, IReadOnlyList<IssueFlag> Flags, TimeSpan? Duration);

    public static void ApplyInlineEdit(
        AttendanceRecord record,
        DateTime? timeIn,
        DateTime? timeOut,
        AttendanceStatus status,
        string note)
    {
        record.CorrectionAction = CorrectionAction.EditTimes;
        record.ReviewerNote = note.Trim();
        record.CorrectionUpdatedAt = DateTime.Now;
        record.CorrectedTimeIn = timeIn;
        record.CorrectedTimeOut = timeOut;
        record.CorrectedStatus = status;
    }

    public static void ApplyInlineEdit(
        AttendanceRecord record,
        DateTime? timeIn,
        DateTime? timeOut,
        AttendanceRules rules,
        string note)
    {
        if (!TryInferInlineEdit(timeIn, timeOut, rules, out var inference, out var error))
        {
            throw new InvalidOperationException(error);
        }

        record.CorrectionAction = CorrectionAction.EditTimes;
        record.ReviewerNote = note.Trim();
        record.CorrectionUpdatedAt = DateTime.Now;
        record.CorrectedTimeIn = timeIn;
        record.CorrectedTimeOut = timeOut;
        record.CorrectedStatus = inference.Status;
    }

    public static bool TryInferInlineEdit(
        DateTime? timeIn,
        DateTime? timeOut,
        AttendanceRules rules,
        out CorrectionInference inference,
        out string error)
    {
        error = string.Empty;

        if (timeIn is null && timeOut is null)
        {
            inference = new CorrectionInference(AttendanceStatus.NoRecord, FlagsForStatus(AttendanceStatus.NoRecord), null);
            return true;
        }

        if (timeIn is null)
        {
            inference = new CorrectionInference(AttendanceStatus.MissingIn, FlagsForStatus(AttendanceStatus.MissingIn), null);
            return true;
        }

        if (timeOut is null)
        {
            inference = new CorrectionInference(AttendanceStatus.MissingOut, FlagsForStatus(AttendanceStatus.MissingOut), null);
            return true;
        }

        var duration = timeOut.Value - timeIn.Value;
        if (duration <= TimeSpan.Zero)
        {
            inference = new CorrectionInference(AttendanceStatus.NeedsReview, FlagsForStatus(AttendanceStatus.NeedsReview), null);
            error = "Time Out must be after Time In.";
            return false;
        }

        if (duration < rules.MinimumWorkDuration)
        {
            inference = new CorrectionInference(AttendanceStatus.TooShort, FlagsForStatus(AttendanceStatus.TooShort), duration);
            return true;
        }

        if (duration > rules.MaximumWorkDuration)
        {
            inference = new CorrectionInference(AttendanceStatus.NeedsReview, FlagsForStatus(AttendanceStatus.NeedsReview), duration);
            return true;
        }

        if (timeOut.Value.Date > timeIn.Value.Date)
        {
            var inTime = TimeOnly.FromDateTime(timeIn.Value).ToTimeSpan();
            var outTime = TimeOnly.FromDateTime(timeOut.Value).ToTimeSpan();
            var status = inTime >= rules.NightPairStart && outTime <= rules.NightEndEnd
                ? AttendanceStatus.LikelyNightShift
                : AttendanceStatus.NeedsReview;
            inference = new CorrectionInference(status, FlagsForStatus(status), duration);
            return true;
        }

        inference = new CorrectionInference(AttendanceStatus.CleanDayShift, FlagsForStatus(AttendanceStatus.CleanDayShift), duration);
        return true;
    }

    public static IReadOnlyList<IssueFlag> FlagsForStatus(AttendanceStatus status)
    {
        return status switch
        {
            AttendanceStatus.CleanDayShift => Array.Empty<IssueFlag>(),
            AttendanceStatus.LikelyNightShift => [IssueFlag.LikelyNightShift],
            AttendanceStatus.NoRecord => [IssueFlag.NeedsReview],
            AttendanceStatus.MissingIn => [IssueFlag.MissingIn, IssueFlag.NeedsReview],
            AttendanceStatus.MissingOut => [IssueFlag.MissingOut, IssueFlag.NeedsReview],
            AttendanceStatus.CarryoverFromPreviousCutoff => [IssueFlag.Carryover, IssueFlag.BoundaryPunch, IssueFlag.NeedsReview],
            AttendanceStatus.CarryoverToNextCutoff => [IssueFlag.Carryover, IssueFlag.BoundaryPunch, IssueFlag.NeedsReview],
            AttendanceStatus.TooShort => [IssueFlag.TooShort, IssueFlag.NeedsReview],
            AttendanceStatus.NeedsReview => [IssueFlag.NeedsReview, IssueFlag.Ambiguous],
            _ => [IssueFlag.NeedsReview]
        };
    }

    public static void Apply(AttendanceRecord record, CorrectionAction action, DateTime? timeIn, DateTime? timeOut, string note)
    {
        record.CorrectionAction = action;
        record.ReviewerNote = note.Trim();
        record.CorrectionUpdatedAt = DateTime.Now;
        record.CorrectedTimeIn = null;
        record.CorrectedTimeOut = null;
        record.CorrectedStatus = null;

        switch (action)
        {
            case CorrectionAction.ApproveAsCorrect:
                record.CorrectedTimeIn = record.TimeIn;
                record.CorrectedTimeOut = record.TimeOut;
                record.CorrectedStatus = record.Status;
                break;
            case CorrectionAction.EditTimes:
                record.CorrectedTimeIn = timeIn;
                record.CorrectedTimeOut = timeOut;
                record.CorrectedStatus = InferStatus(record.Status, timeIn, timeOut);
                break;
            case CorrectionAction.MarkMissingIn:
                record.CorrectedTimeOut = timeOut ?? record.TimeOut ?? record.TimeIn;
                record.CorrectedStatus = AttendanceStatus.MissingIn;
                break;
            case CorrectionAction.MarkMissingOut:
                record.CorrectedTimeIn = timeIn ?? record.TimeIn ?? record.TimeOut;
                record.CorrectedStatus = AttendanceStatus.MissingOut;
                break;
            case CorrectionAction.MarkCarryoverFromPreviousCutoff:
                record.CorrectedTimeOut = timeOut ?? record.TimeOut ?? record.TimeIn;
                record.CorrectedStatus = AttendanceStatus.CarryoverFromPreviousCutoff;
                break;
            case CorrectionAction.MarkCarryoverToNextCutoff:
                record.CorrectedTimeIn = timeIn ?? record.TimeIn ?? record.TimeOut;
                record.CorrectedStatus = AttendanceStatus.CarryoverToNextCutoff;
                break;
            case CorrectionAction.IgnoreDuplicateTap:
            case CorrectionAction.ApproveExtraPunches:
                record.CorrectedTimeIn = record.TimeIn;
                record.CorrectedTimeOut = record.TimeOut;
                record.CorrectedStatus = record.Status;
                break;
            case CorrectionAction.MarkNeedsReview:
                record.CorrectedTimeIn = timeIn;
                record.CorrectedTimeOut = timeOut;
                record.CorrectedStatus = AttendanceStatus.NeedsReview;
                break;
            case CorrectionAction.None:
            default:
                record.CorrectionUpdatedAt = null;
                break;
        }
    }

    public static CorrectionRecord ToCorrectionRecord(AttendanceRecord record, string importKey)
    {
        return new CorrectionRecord(
            importKey,
            CorrectionKey.ForRecord(record),
            record.EmployeeId,
            record.WorkDate,
            record.RawPunches,
            record.Status,
            record.FlagText,
            record.TimeIn,
            record.TimeOut,
            record.CorrectionAction,
            record.CorrectedStatus,
            record.CorrectedTimeIn,
            record.CorrectedTimeOut,
            record.ReviewerNote,
            record.CorrectionUpdatedAt ?? DateTime.Now);
    }

    public static void ApplySavedCorrection(AttendanceRecord record, CorrectionRecord correction)
    {
        record.CorrectionAction = correction.Action;
        record.CorrectedStatus = correction.CorrectedStatus;
        record.CorrectedTimeIn = correction.CorrectedTimeIn;
        record.CorrectedTimeOut = correction.CorrectedTimeOut;
        record.ReviewerNote = correction.ReviewerNote;
        record.CorrectionUpdatedAt = correction.UpdatedAt;
    }

    private static AttendanceStatus InferStatus(AttendanceStatus fallback, DateTime? timeIn, DateTime? timeOut)
    {
        if (timeIn is null && timeOut is not null)
        {
            return AttendanceStatus.MissingIn;
        }

        if (timeIn is not null && timeOut is null)
        {
            return AttendanceStatus.MissingOut;
        }

        if (timeIn is not null && timeOut is not null && timeOut.Value.Date > timeIn.Value.Date)
        {
            return AttendanceStatus.LikelyNightShift;
        }

        return timeIn is not null && timeOut is not null ? AttendanceStatus.CleanDayShift : fallback;
    }
}

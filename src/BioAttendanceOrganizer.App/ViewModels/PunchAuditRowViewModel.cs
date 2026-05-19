using System.Globalization;
using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.App.ViewModels;

public sealed class PunchAuditRowViewModel
{
    private PunchAuditRowViewModel(
        string employeeId,
        DateTime updatedAt,
        string actionText,
        DateOnly workDate,
        string reason,
        string beforeAfterText)
    {
        EmployeeId = employeeId;
        UpdatedAt = updatedAt;
        ActionText = actionText;
        DateText = workDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Reason = string.IsNullOrWhiteSpace(reason) ? "-" : reason;
        BeforeAfterText = beforeAfterText;
    }

    public PunchAuditRowViewModel(PunchCorrectionAuditEntry entry)
        : this(
            entry.EmployeeId,
            entry.UpdatedAt,
            entry.Action.ToString(),
            entry.WorkDate,
            entry.Reason,
            $"{entry.BeforeValue} -> {entry.AfterValue}")
    {
        Entry = entry;
    }

    public PunchAuditRowViewModel(InlineCorrectionAuditEntry entry)
        : this(
            entry.EmployeeId,
            entry.UpdatedAt,
            "DTR Correction",
            entry.WorkDate,
            entry.ReviewerNote,
            FormatInlineCorrection(entry))
    {
        InlineEntry = entry;
    }

    public PunchCorrectionAuditEntry? Entry { get; }
    public InlineCorrectionAuditEntry? InlineEntry { get; }
    public string EmployeeId { get; }
    public DateTime UpdatedAt { get; }
    public string WhenText => UpdatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    public string ActionText { get; }
    public string DateText { get; }
    public string Reason { get; }
    public string BeforeAfterText { get; }

    private static string FormatInlineCorrection(InlineCorrectionAuditEntry entry)
    {
        var timeIn = entry.FinalTimeIn?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "Missing IN";
        var timeOut = entry.FinalTimeOut?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "Missing OUT";
        return $"{timeIn} / {timeOut} -> {FormatStatus(entry.FinalStatus)}";
    }

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

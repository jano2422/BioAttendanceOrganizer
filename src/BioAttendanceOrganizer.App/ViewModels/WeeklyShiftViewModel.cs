using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.App.ViewModels;

public sealed class WeeklyShiftViewModel
{
    public WeeklyShiftViewModel(AttendanceRecord record)
    {
        Record = record;
    }

    public AttendanceRecord Record { get; }
    public string PrimaryText => string.IsNullOrWhiteSpace(Record.DurationText)
        ? Record.FinalStatusText
        : $"{Record.DurationText} hrs";
    public string TimeRangeText => BuildTimeRangeText(Record);
    public string WorkDateText => Record.WorkDateText;
    public string TimeInText => Record.FinalTimeIn?.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "Missing IN";
    public string TimeOutText => Record.FinalTimeOut?.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) ?? "Missing OUT";
    public string HoursText => PrimaryText;
    public string StatusText => Record.FinalStatusText;
    public string BadgeText => Record.FinalStatus == AttendanceStatus.LikelyNightShift ||
                               Record.FinalStatus == AttendanceStatus.CarryoverFromPreviousCutoff ||
                               Record.FinalStatus == AttendanceStatus.CarryoverToNextCutoff
        ? string.Empty
        : Record.IsOvernightShift
            ? "Extended"
            : string.Empty;
    public bool HasBadge => !string.IsNullOrWhiteSpace(BadgeText);
    public string RawPunchSummary => BuildRawPunchSummary(Record.RawPunches);
    public bool HasRawPunchSummary => !string.IsNullOrWhiteSpace(RawPunchSummary);

    private static string BuildTimeRangeText(AttendanceRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.ShiftRangeText))
        {
            return record.ShiftRangeText;
        }

        if (record.FinalTimeIn.HasValue)
        {
            return "IN " + record.FinalTimeIn.Value.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (record.FinalTimeOut.HasValue)
        {
            return "OUT " + record.FinalTimeOut.Value.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }

        return "No punches";
    }

    private static string BuildRawPunchSummary(string rawPunches)
    {
        if (string.IsNullOrWhiteSpace(rawPunches))
        {
            return "No raw punches";
        }

        var parts = rawPunches
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(4)
            .ToList();
        if (parts.Count == 0)
        {
            return rawPunches.Trim();
        }

        var suffix = rawPunches.Count(ch => ch is ',' or ';' or '\n') >= parts.Count ? " ..." : string.Empty;
        return string.Join(" | ", parts) + suffix;
    }
}

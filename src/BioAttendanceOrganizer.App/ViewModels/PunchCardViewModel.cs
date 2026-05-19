using System.Globalization;
using BioAttendanceOrganizer.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BioAttendanceOrganizer.App.ViewModels;

public sealed class PunchCardViewModel : ObservableObject
{
    private readonly PunchAssignment? _assignment;

    public PunchCardViewModel(PunchAssignment assignment)
    {
        _assignment = assignment;
    }

    private PunchCardViewModel(string placeholderText, AttendanceStatus issueStatus, DateOnly workDate)
    {
        IsPlaceholder = true;
        PlaceholderText = placeholderText;
        IssueStatus = issueStatus;
        _workDate = workDate;
    }

    public static PunchCardViewModel CreatePlaceholder(string placeholderText, AttendanceStatus issueStatus, DateOnly workDate)
    {
        return new PunchCardViewModel(placeholderText, issueStatus, workDate);
    }

    private readonly DateOnly _workDate;

    public bool IsPlaceholder { get; }
    public string PlaceholderText { get; } = string.Empty;
    public AttendanceStatus IssueStatus { get; }

    public PunchAssignment? Assignment => _assignment;
    public string SourcePunchKey => IsPlaceholder ? $"Placeholder_{IssueStatus}_{_workDate:O}" : _assignment!.SourcePunchKey;
    public string EmployeeId => IsPlaceholder ? string.Empty : _assignment!.EmployeeId;
    public string EmployeeName => IsPlaceholder ? string.Empty : _assignment!.EmployeeName;
    public DateOnly WorkDate => IsPlaceholder ? _workDate : _assignment!.WorkDate;
    public DateTime Timestamp => IsPlaceholder ? _workDate.ToDateTime(TimeOnly.MinValue) : _assignment!.Timestamp;
    public PunchType PunchType => IsPlaceholder ? PunchType.Unpaired : _assignment!.PunchType;
    public bool IsDeleted => !IsPlaceholder && _assignment!.IsDeleted;
    public bool IsChanged => !IsPlaceholder && _assignment!.IsChanged;

    public string TimeText => IsPlaceholder ? PlaceholderText : _assignment!.Timestamp.ToString("HH:mm", CultureInfo.InvariantCulture);
    public string TypeText => IsPlaceholder ? "Missing" : (_assignment!.PunchType == PunchType.Unpaired ? "Punch" : _assignment.PunchType.ToString().ToUpperInvariant());
    public string DetailText => IsPlaceholder ? "Expected Punch" : (_assignment!.IsDeleted ? "Deleted" : _assignment.EmployeeName);
    public string StateText => IsPlaceholder ? "Missing" : (_assignment!.IsDeleted ? "Deleted" : _assignment.IsChanged ? "Changed" : TypeText);
    public string RecordHoursText { get; set; } = string.Empty;
    
    public string ChipBrush => IsPlaceholder
        ? "#FFFBEB" // Amber-50 for warning
        : _assignment!.IsDeleted
        ? "#FEE2E2"
        : _assignment.IsChanged
            ? "#FEF3C7"
            : _assignment.PunchType switch
            {
                PunchType.In => "#DCFCE7",
                PunchType.Out => "#DBEAFE",
                _ => "#F1F5F9"
            };

    public string ChipBorderBrush => IsPlaceholder
        ? "#F59E0B" // Amber-500
        : _assignment!.IsDeleted
        ? "#DC2626"
        : _assignment.IsChanged
            ? "#D97706"
            : _assignment.PunchType switch
            {
                PunchType.In => "#16A34A",
                PunchType.Out => "#2563EB",
                _ => "#CBD5E1"
            };

    public void Refresh()
    {
        OnPropertyChanged(string.Empty);
    }
}

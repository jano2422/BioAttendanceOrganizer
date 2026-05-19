using BioAttendanceOrganizer.Core.Models;

namespace BioAttendanceOrganizer.App.ViewModels;

public sealed class AttendanceStatusOption
{
    public AttendanceStatusOption(AttendanceStatus status, string label)
    {
        Status = status;
        Label = label;
    }

    public AttendanceStatus Status { get; }
    public string Label { get; }
}

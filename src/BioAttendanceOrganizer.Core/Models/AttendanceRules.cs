namespace BioAttendanceOrganizer.Core.Models;

public sealed class AttendanceRules
{
    public static AttendanceRules CreateDefault() => new();

    public TimeSpan DuplicateTapWindow { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan MinimumWorkDuration { get; init; } = TimeSpan.FromHours(4);
    public TimeSpan MaximumWorkDuration { get; init; } = TimeSpan.FromHours(20);
    public TimeSpan MorningBoundaryEnd { get; init; } = new(10, 0, 0);
    public TimeSpan DayStartEnd { get; init; } = new(12, 0, 0);
    public TimeSpan DayEndStart { get; init; } = new(12, 0, 0);
    public TimeSpan DayEndEnd { get; init; } = new(20, 0, 0);
    public TimeSpan NightPairStart { get; init; } = new(4, 0, 0);
    public TimeSpan StrongNightStart { get; init; } = new(16, 0, 0);
    public TimeSpan NightEndEnd { get; init; } = new(10, 0, 0);
    public bool EnableCarryoverBoundaryDetection { get; init; } = true;
    public bool AutoApproveCleanNightShifts { get; init; } = true;
}

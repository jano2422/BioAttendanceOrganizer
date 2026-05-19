using System.Collections.ObjectModel;
using System.Globalization;

namespace BioAttendanceOrganizer.App.ViewModels;

public sealed class WeekDayColumnViewModel
{
    public WeekDayColumnViewModel(DateOnly date)
    {
        Date = date;
    }

    public DateOnly Date { get; }
    public string DayName => Date.ToDateTime(TimeOnly.MinValue).ToString("ddd", CultureInfo.InvariantCulture);
    public string DateLabel => Date.ToString("MMM d", CultureInfo.InvariantCulture);
    public bool HasShifts => Shifts.Count > 0;
    public ObservableCollection<WeeklyShiftViewModel> Shifts { get; } = new();
    public ObservableCollection<object> Items { get; } = new();
}

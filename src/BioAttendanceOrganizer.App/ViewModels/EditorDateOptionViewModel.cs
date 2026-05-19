using System.Globalization;

namespace BioAttendanceOrganizer.App.ViewModels;

public sealed class EditorDateOptionViewModel
{
    public EditorDateOptionViewModel(DateTime date, string label)
    {
        Date = date.Date;
        Label = label;
    }

    public DateTime Date { get; }
    public string Label { get; }

    public static EditorDateOptionViewModel For(DateOnly date, string label)
    {
        return new EditorDateOptionViewModel(
            date.ToDateTime(TimeOnly.MinValue),
            $"{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} - {label}");
    }
}
